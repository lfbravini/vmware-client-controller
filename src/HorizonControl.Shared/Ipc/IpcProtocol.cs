using System.Text.Json.Serialization;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;

namespace HorizonControl.Shared.Ipc;

/// <summary>
/// Named pipe: <c>\\.\pipe\HorizonWatchdogIPC</c>.
/// Framing: 4-byte little-endian payload length, then UTF-8 JSON <see cref="IpcEnvelope"/>.
/// </summary>
public static class IpcDefaults
{
    public const string PipeName = "HorizonWatchdogIPC";

    public const int MaxFrameBytes = 1024 * 1024;
}

public enum IpcMessageType
{
    Ping = 0,
    Pong = 1,
    InstanceInventory = 2,
    InstanceDegraded = 3,
    ExecuteLocalCommand = 4,
    CommandResult = 5,
    PolicyPush = 6
}

public sealed class IpcEnvelope
{
    public IpcMessageType Type { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTimeOffset SentAtUtc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstanceInventoryIpcMessage? Inventory { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstanceDegradedIpcMessage? Degraded { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecuteLocalCommandIpcMessage? Execute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CommandResultIpcMessage? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Policy.OuPolicy? Policy { get; set; }
}

public sealed class InstanceInventoryIpcMessage
{
    public IReadOnlyList<ManagedInstanceSnapshot> Instances { get; set; } = [];
}

public sealed class InstanceDegradedIpcMessage
{
    public Guid InstanceId { get; set; }

    public ManagedInstanceState State { get; set; }

    public string? WindowTitle { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset DetectedAtUtc { get; set; }
}

public sealed class ExecuteLocalCommandIpcMessage
{
    public AgentCommand Command { get; set; } = new();
}

public sealed class CommandResultIpcMessage
{
    public Guid CommandId { get; set; }

    public bool Success { get; set; }

    public bool IgnoredReplay { get; set; }

    public bool Expired { get; set; }

    public string? Message { get; set; }
}
