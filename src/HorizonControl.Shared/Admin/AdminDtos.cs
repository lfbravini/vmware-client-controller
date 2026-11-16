using HorizonControl.Shared.Enums;

namespace HorizonControl.Shared.Admin;

public sealed class EndpointListItem
{
    public Guid AgentUuid { get; set; }

    public Guid TenantId { get; set; }

    public Guid OuId { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public bool IsDomainJoined { get; set; }

    public string? AgentVersion { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class ManagedInstanceListItem
{
    public Guid InstanceId { get; set; }

    public Guid AgentUuid { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public string BrokerFqdn { get; set; } = string.Empty;

    public string? HorizonUser { get; set; }

    public int RootPid { get; set; }

    public string JobObjectName { get; set; } = string.Empty;

    public ManagedInstanceState State { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class RestartInstanceRequest
{
    public Guid InstanceId { get; set; }

    public int TtlSec { get; set; } = 120;
}

public sealed class RestartInstanceResponse
{
    public Guid CommandId { get; set; }

    public Guid InstanceId { get; set; }

    public Guid AgentUuid { get; set; }
}
