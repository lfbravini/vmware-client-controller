using HorizonControl.Shared.Enums;

namespace HorizonControl.Shared.Agent;

public sealed class AgentHeartbeatRequest
{
    public Guid AgentUuid { get; set; }

    public string? Hostname { get; set; }

    public string? LocalIp { get; set; }

    public string? PublicIp { get; set; }

    public string? AgentVersion { get; set; }

    public string? HorizonClientVersion { get; set; }

    public IReadOnlyList<ManagedInstanceSnapshot> Instances { get; set; } = [];
}

public sealed class ManagedInstanceSnapshot
{
    public Guid InstanceId { get; set; }

    public string BrokerFqdn { get; set; } = string.Empty;

    public string? HorizonUser { get; set; }

    public string? TargetApp { get; set; }

    /// <summary>Host-local telemetry only — never use as command targeting authority.</summary>
    public int RootPid { get; set; }

    public string JobObjectName { get; set; } = string.Empty;

    public ManagedInstanceState State { get; set; }

    public IReadOnlyList<SeamlessWindowSnapshot> SeamlessWindows { get; set; } = [];

    public IReadOnlyList<ChildProcessSnapshot> ChildProcesses { get; set; } = [];

    public DateTimeOffset StartedAtUtc { get; set; }
}

public sealed class SeamlessWindowSnapshot
{
    public long Hwnd { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsResponsive { get; set; }

    public int ConsecutiveHangFailures { get; set; }
}

public sealed class ChildProcessSnapshot
{
    public int Pid { get; set; }

    public string ProcessName { get; set; } = string.Empty;
}

public sealed class InstanceDegradedEvent
{
    public Guid AgentUuid { get; set; }

    public Guid InstanceId { get; set; }

    public ManagedInstanceState State { get; set; }

    public string? WindowTitle { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset DetectedAtUtc { get; set; }
}

public sealed class AgentCommandAck
{
    public Guid CommandId { get; set; }

    public bool Success { get; set; }

    /// <summary>True when a duplicate command_id was ignored (idempotent replay).</summary>
    public bool IgnoredReplay { get; set; }

    /// <summary>True when the command was dropped because TTL expired.</summary>
    public bool Expired { get; set; }

    public string? Message { get; set; }
}
