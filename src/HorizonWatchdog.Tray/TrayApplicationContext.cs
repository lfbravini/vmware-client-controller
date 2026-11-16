using System.Collections.Concurrent;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;
using HorizonControl.Shared.Ipc;
using HorizonWatchdog.Tray.Ipc;
using HorizonWatchdog.Tray.Monitoring;

namespace HorizonWatchdog.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly InstanceManager _instances = new();
    private readonly TrayPipeClient _pipe = new();
    private readonly ConcurrentDictionary<Guid, byte> _seenCommands = new();
    private readonly int _consecutiveRequired = 3;

    public TrayApplicationContext()
    {
        _icon = new NotifyIcon
        {
            Text = "Horizon Watchdog Tray",
            Visible = true,
            Icon = SystemIcons.Application,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitThread());

        _pipe.MessageReceived += OnPipeMessageAsync;
        _ = ConnectPipeAsync();

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private async Task ConnectPipeAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            try
            {
                await _pipe.ConnectAsync(CancellationToken.None);
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }
    }

    private async Task TickAsync()
    {
        try
        {
            _instances.DiscoverAndTrack();
            _ = InstanceManager.ExistingLogDirs();

            var hangs = _instances.CheckHangs(_consecutiveRequired);
            foreach (var hit in hangs.DistinctBy(h => h.Instance.InstanceId))
            {
                await _pipe.SendAsync(new IpcEnvelope
                {
                    Type = IpcMessageType.InstanceDegraded,
                    CorrelationId = Guid.NewGuid(),
                    SentAtUtc = DateTimeOffset.UtcNow,
                    Degraded = new InstanceDegradedIpcMessage
                    {
                        InstanceId = hit.Instance.InstanceId,
                        State = ManagedInstanceState.AppHung,
                        WindowTitle = hit.Title,
                        Detail = "SendMessageTimeout failed consecutively",
                        DetectedAtUtc = DateTimeOffset.UtcNow
                    }
                }, CancellationToken.None);
            }

            await _pipe.SendAsync(new IpcEnvelope
            {
                Type = IpcMessageType.InstanceInventory,
                CorrelationId = Guid.NewGuid(),
                SentAtUtc = DateTimeOffset.UtcNow,
                Inventory = new InstanceInventoryIpcMessage { Instances = _instances.Snapshot() }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async Task OnPipeMessageAsync(IpcEnvelope message)
    {
        if (message.Type != IpcMessageType.ExecuteLocalCommand || message.Execute?.Command is null)
        {
            return;
        }

        var command = message.Execute.Command;
        var result = ExecuteCommand(command);
        await _pipe.SendAsync(new IpcEnvelope
        {
            Type = IpcMessageType.CommandResult,
            CorrelationId = command.CommandId,
            SentAtUtc = DateTimeOffset.UtcNow,
            Result = result
        }, CancellationToken.None);
    }

    private CommandResultIpcMessage ExecuteCommand(AgentCommand command)
    {
        if (command.IsExpired(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30)))
        {
            return new CommandResultIpcMessage
            {
                CommandId = command.CommandId,
                Success = true,
                Expired = true,
                Message = "Expired"
            };
        }

        if (!_seenCommands.TryAdd(command.CommandId, 0))
        {
            return new CommandResultIpcMessage
            {
                CommandId = command.CommandId,
                Success = true,
                IgnoredReplay = true,
                Message = "Replay ignored"
            };
        }

        if (command.Type is AgentCommandType.RestartClientInstance or AgentCommandType.KillClientInstance)
        {
            if (command.InstanceId is not Guid id)
            {
                return new CommandResultIpcMessage
                {
                    CommandId = command.CommandId,
                    Success = false,
                    Message = "Missing instance_id"
                };
            }

            try
            {
                var ok = _instances.TerminateInstance(id);
                return new CommandResultIpcMessage
                {
                    CommandId = command.CommandId,
                    Success = ok,
                    Message = ok ? "TerminateJobObject completed" : "Instance not found"
                };
            }
            catch (Exception ex)
            {
                return new CommandResultIpcMessage
                {
                    CommandId = command.CommandId,
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        return new CommandResultIpcMessage
        {
            CommandId = command.CommandId,
            Success = false,
            Message = $"Unsupported command {command.Type} in MVP"
        };
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _ = _pipe.DisposeAsync();
        base.ExitThreadCore();
    }
}
