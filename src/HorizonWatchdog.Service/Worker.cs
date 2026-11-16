using System.Collections.Concurrent;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;
using HorizonControl.Shared.Ipc;
using HorizonWatchdog.Service.ControlPlane;
using HorizonWatchdog.Service.Ipc;

namespace HorizonWatchdog.Service;

public sealed class Worker(
    ILogger<Worker> logger,
    ControlPlaneClient controlPlane,
    NamedPipeServer pipeServer) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> _seenCommands = new();
    private readonly ConcurrentDictionary<Guid, ManagedInstanceSnapshot> _instances = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = Environment.GetEnvironmentVariable("HORIZON_ENROLLMENT_TOKEN") ?? "lab-enroll-token-change-me";
        try
        {
            await controlPlane.EnsureEnrolledAsync(token, stoppingToken);
            await controlPlane.ConnectSignalRAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Initial enroll/SignalR failed; will retry via heartbeat loop");
        }

        controlPlane.CommandReceived += OnCommandAsync;
        pipeServer.MessageReceived += OnPipeMessageAsync;
        pipeServer.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (controlPlane.AgentUuid == Guid.Empty)
                {
                    await controlPlane.EnsureEnrolledAsync(token, stoppingToken);
                    await controlPlane.ConnectSignalRAsync(stoppingToken);
                }

                var heartbeat = new AgentHeartbeatRequest
                {
                    AgentUuid = controlPlane.AgentUuid,
                    Hostname = Environment.MachineName,
                    AgentVersion = "1.0.0-mvp",
                    Instances = _instances.Values.ToList()
                };
                await controlPlane.SendHeartbeatAsync(heartbeat, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task OnCommandAsync(AgentCommand command)
    {
        var skew = TimeSpan.FromSeconds(30);
        if (command.IsExpired(DateTimeOffset.UtcNow, skew))
        {
            await controlPlane.AckCommandAsync(new AgentCommandAck
            {
                CommandId = command.CommandId,
                Success = true,
                Expired = true,
                Message = "Dropped expired command"
            }, CancellationToken.None);
            return;
        }

        if (!_seenCommands.TryAdd(command.CommandId, 0))
        {
            await controlPlane.AckCommandAsync(new AgentCommandAck
            {
                CommandId = command.CommandId,
                Success = true,
                IgnoredReplay = true,
                Message = "Ignored replay"
            }, CancellationToken.None);
            return;
        }

        var envelope = new IpcEnvelope
        {
            Type = IpcMessageType.ExecuteLocalCommand,
            CorrelationId = command.CommandId,
            SentAtUtc = DateTimeOffset.UtcNow,
            Execute = new ExecuteLocalCommandIpcMessage { Command = command }
        };

        try
        {
            await pipeServer.SendAsync(envelope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch command {CommandId} to Tray", command.CommandId);
        }
    }

    private async Task OnPipeMessageAsync(IpcEnvelope message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case IpcMessageType.InstanceInventory:
                _instances.Clear();
                foreach (var inst in message.Inventory?.Instances ?? [])
                {
                    _instances[inst.InstanceId] = inst;
                }

                break;
            case IpcMessageType.InstanceDegraded:
                if (message.Degraded is not null)
                {
                    await controlPlane.ReportDegradedAsync(new InstanceDegradedEvent
                    {
                        AgentUuid = controlPlane.AgentUuid,
                        InstanceId = message.Degraded.InstanceId,
                        State = message.Degraded.State,
                        WindowTitle = message.Degraded.WindowTitle,
                        Detail = message.Degraded.Detail,
                        DetectedAtUtc = message.Degraded.DetectedAtUtc
                    }, cancellationToken);

                    if (controlPlane.CurrentPolicy.Remediation.AutoRemediate &&
                        message.Degraded.State == ManagedInstanceState.AppHung)
                    {
                        await OnCommandAsync(new AgentCommand
                        {
                            CommandId = Guid.NewGuid(),
                            Type = AgentCommandType.RestartClientInstance,
                            InstanceId = message.Degraded.InstanceId,
                            TtlSec = 120,
                            IssuedAtUtc = DateTimeOffset.UtcNow
                        });
                    }
                }

                break;
            case IpcMessageType.CommandResult:
                if (message.Result is not null)
                {
                    await controlPlane.AckCommandAsync(new AgentCommandAck
                    {
                        CommandId = message.Result.CommandId,
                        Success = message.Result.Success,
                        IgnoredReplay = message.Result.IgnoredReplay,
                        Expired = message.Result.Expired,
                        Message = message.Result.Message
                    }, cancellationToken);
                }

                break;
        }
    }
}
