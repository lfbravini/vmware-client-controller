using System.Collections.Concurrent;
using System.Text.Json;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;
using HorizonControl.Shared.Ipc;
using HorizonControl.Shared.Policy;
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
    private OuPolicy _policy = LoadCachedPolicy();
    private readonly ConcurrentQueue<DateTimeOffset> _restartTimes = new();

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

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1000, _policy.Monitoring.CheckIntervalSec * 1000)
        };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private static OuPolicy LoadCachedPolicy()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HorizonWatchdog",
                "policy.json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<OuPolicy>(File.ReadAllText(path)) ?? new OuPolicy();
            }
        }
        catch
        {
            // use defaults
        }

        return new OuPolicy();
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
            var intervalMs = Math.Max(1000, _policy.Monitoring.CheckIntervalSec * 1000);
            if (_timer.Interval != intervalMs)
            {
                _timer.Interval = intervalMs;
            }

            _instances.DiscoverAndTrack();
            _ = InstanceManager.ExistingLogDirs();

            var required = Math.Max(1, _policy.Monitoring.ConsecutiveFailuresRequired);
            var hangs = _instances.CheckHangs(required);
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
        if (message.Type == IpcMessageType.PolicyPush && message.Policy is not null)
        {
            _policy = message.Policy;
            return;
        }

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
                if (command.Type == AgentCommandType.RestartClientInstance && !AllowRestart())
                {
                    return new CommandResultIpcMessage
                    {
                        CommandId = command.CommandId,
                        Success = false,
                        Message = $"Blocked by max_restarts_per_hour={_policy.Remediation.MaxRestartsPerHour}"
                    };
                }

                var relaunch = command.Type == AgentCommandType.RestartClientInstance;
                var ok = _instances.TerminateInstance(id, relaunch);
                if (ok && relaunch)
                {
                    _restartTimes.Enqueue(DateTimeOffset.UtcNow);
                    if (_policy.Remediation.NotifyUserOnRestart)
                    {
                        _icon.ShowBalloonTip(
                            4000,
                            "Horizon Watchdog",
                            "Client instance restarted (Job Object terminate + relaunch).",
                            ToolTipIcon.Info);
                    }
                }

                return new CommandResultIpcMessage
                {
                    CommandId = command.CommandId,
                    Success = ok,
                    Message = ok
                        ? (relaunch ? "TerminateJobObject + relaunch attempted" : "TerminateJobObject completed")
                        : "Instance not found"
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
            Message = $"Unsupported command {command.Type}"
        };
    }

    private bool AllowRestart()
    {
        var max = Math.Max(0, _policy.Remediation.MaxRestartsPerHour);
        if (max == 0)
        {
            return true;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        while (_restartTimes.TryPeek(out var t) && t < cutoff)
        {
            _restartTimes.TryDequeue(out _);
        }

        return _restartTimes.Count < max;
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
