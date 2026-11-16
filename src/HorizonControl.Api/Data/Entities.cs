namespace HorizonControl.Api.Data;

public sealed class Tenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<VirtualOu> VirtualOus { get; set; } = [];
}

public sealed class VirtualOu
{
    public Guid OuId { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = "root";
    public string PolicyOverrideJson { get; set; } = "{}";
}

public sealed class EnrollmentToken
{
    public Guid TokenId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OuId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UseCount { get; set; }
}

public sealed class FleetEndpoint
{
    public Guid AgentUuid { get; set; }
    public Guid TenantId { get; set; }
    public Guid OuId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string HardwareHash { get; set; } = string.Empty;
    public string AgentSecretHash { get; set; } = string.Empty;
    public bool IsDomainJoined { get; set; }
    public string? AdOuDn { get; set; }
    public string? OsVersion { get; set; }
    public string? AgentVersion { get; set; }
    public string? HorizonClientVersion { get; set; }
    public string? RustdeskId { get; set; }
    public string? LocalIp { get; set; }
    public string? PublicIp { get; set; }
    public string? MacAddress { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class ManagedInstance
{
    public Guid InstanceId { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentUuid { get; set; }
    public string BrokerFqdn { get; set; } = string.Empty;
    public string? HorizonUser { get; set; }
    public int RootPid { get; set; }
    public string JobObjectName { get; set; } = string.Empty;
    public string State { get; set; } = "HEALTHY";
    public string SeamlessWindowsJson { get; set; } = "[]";
    public string ChildProcessesJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditLog
{
    public long AuditId { get; set; }
    public Guid TenantId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProcessedCommand
{
    public Guid CommandId { get; set; }
    public Guid AgentUuid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
