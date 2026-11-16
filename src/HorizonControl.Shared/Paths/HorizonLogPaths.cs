namespace HorizonControl.Shared.Paths;

/// <summary>
/// Candidate local directories for Horizon / Omnissa VDM and Blast protocol logs.
/// Probe all; any may be absent depending on client brand and version.
/// </summary>
public static class HorizonLogPaths
{
    public const string VmwareVdmRelative = @"VMware\VDM\logs";
    public const string OmnissaVdmRelative = @"Omnissa\VDM\logs";
    public const string OmnissaHorizonRelative = @"Omnissa\Horizon\logs";

    /// <summary>
    /// Returns absolute candidate log directories under the given LocalAppData root
    /// (typically Environment.GetFolderPath(SpecialFolder.LocalApplicationData)).
    /// </summary>
    public static IReadOnlyList<string> GetLocalAppDataCandidates(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);
        return
        [
            Path.Combine(localAppDataRoot, VmwareVdmRelative),
            Path.Combine(localAppDataRoot, OmnissaVdmRelative),
            Path.Combine(localAppDataRoot, OmnissaHorizonRelative)
        ];
    }
}
