using HorizonControl.Shared.Policy;

namespace HorizonControl.Shared.Agent;

public sealed class AgentEnrollRequest
{
    public string EnrollmentToken { get; set; } = string.Empty;

    public Guid? AgentUuid { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public string HardwareHash { get; set; } = string.Empty;

    public bool IsDomainJoined { get; set; }

    public string? AdOuDn { get; set; }

    public string? OsVersion { get; set; }

    public string? AgentVersion { get; set; }

    public string? HorizonClientVersion { get; set; }

    public string? LocalIp { get; set; }

    public string? MacAddress { get; set; }

    /// <summary>When true, replace any existing secret for the same tenant + hardware hash.</summary>
    public bool RotateSecret { get; set; }
}

public sealed class AgentEnrollResponse
{
    public Guid AgentUuid { get; set; }

    public Guid TenantId { get; set; }

    public Guid OuId { get; set; }

    /// <summary>Plaintext secret returned once; agent stores via DPAPI. Never logged by the API.</summary>
    public string AgentSecret { get; set; } = string.Empty;

    public OuPolicy Policy { get; set; } = new();
}
