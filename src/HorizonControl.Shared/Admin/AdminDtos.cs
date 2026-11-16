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

    public bool IsLive { get; set; }

    public string? CorrelatedBrokerSessionId { get; set; }
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

public sealed class TenantListItem
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class VirtualOuListItem
{
    public Guid OuId { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

public sealed class EnrollmentTokenListItem
{
    public Guid TokenId { get; set; }

    public Guid TenantId { get; set; }

    public Guid OuId { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int? MaxUses { get; set; }

    public int UseCount { get; set; }
}

public sealed class CreateEnrollmentTokenRequest
{
    public Guid TenantId { get; set; }

    public Guid OuId { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int? MaxUses { get; set; }

    /// <summary>Optional plaintext; generated if omitted.</summary>
    public string? PlainToken { get; set; }
}

public sealed class CreateEnrollmentTokenResponse
{
    public Guid TokenId { get; set; }

    public Guid TenantId { get; set; }

    public Guid OuId { get; set; }

    /// <summary>Shown once at creation; only the hash is stored.</summary>
    public string PlainToken { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int? MaxUses { get; set; }
}

public sealed class OuPolicyUpdateRequest
{
    public Guid OuId { get; set; }

    public Policy.OuPolicy Policy { get; set; } = new();
}

public sealed class CommandListItem
{
    public Guid CommandId { get; set; }

    public Guid AgentUuid { get; set; }

    public Guid? InstanceId { get; set; }

    public bool Success { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class HorizonBrokerListItem
{
    public Guid BrokerId { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string PublicFqdn { get; set; } = string.Empty;

    public int PollIntervalSec { get; set; }

    public DateTimeOffset? LastPollAtUtc { get; set; }

    public string? LastPollError { get; set; }
}

public sealed class UpsertHorizonBrokerRequest
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string PublicFqdn { get; set; } = string.Empty;

    /// <summary>Lab/dev credential blob (stored as-is for mock brokers; production should encrypt).</summary>
    public string? Credentials { get; set; }

    public int PollIntervalSec { get; set; } = 30;
}

public sealed class BrokerSessionListItem
{
    public Guid SnapshotId { get; set; }

    public Guid BrokerId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public string? DnsName { get; set; }

    public string? BrokerFqdn { get; set; }

    public DateTimeOffset ObservedAtUtc { get; set; }

    public Guid? CorrelatedInstanceId { get; set; }

    public string? CorrelatedHostname { get; set; }
}
