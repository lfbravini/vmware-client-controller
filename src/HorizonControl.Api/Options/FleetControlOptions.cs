namespace HorizonControl.Api.Options;

public sealed class FleetControlOptions
{
    public const string SectionName = "FleetControl";

    public const string DefaultAdminApiKey = "lab-admin-key-change-me";

    public const string DefaultLabEnrollmentToken = "lab-enroll-token-change-me";

    public string AdminApiKey { get; set; } = DefaultAdminApiKey;

    /// <summary>Plaintext lab enrollment token; only the hash is persisted.</summary>
    public string LabEnrollmentToken { get; set; } = DefaultLabEnrollmentToken;

    public string LabTenantName { get; set; } = "Lab Tenant";

    public string LabTenantSlug { get; set; } = "lab";

    /// <summary>When true, Production may keep documented lab default secrets (Portainer lab only).</summary>
    public bool AllowLabDefaults { get; set; }
}
