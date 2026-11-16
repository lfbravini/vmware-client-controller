namespace HorizonControl.Api.Options;

public sealed class FleetControlOptions
{
    public const string SectionName = "FleetControl";

    public string AdminApiKey { get; set; } = "lab-admin-key-change-me";

    /// <summary>Plaintext lab enrollment token; only the hash is persisted.</summary>
    public string LabEnrollmentToken { get; set; } = "lab-enroll-token-change-me";

    public string LabTenantName { get; set; } = "Lab Tenant";

    public string LabTenantSlug { get; set; } = "lab";
}
