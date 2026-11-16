using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace HorizonWatchdog.Service.Identity;

public sealed class AgentIdentityStore(ILogger<AgentIdentityStore> logger)
{
    private const string RegPath = @"SOFTWARE\HorizonWatchdog";

    public Guid GetOrCreateAgentUuid()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath);
        var existing = key.GetValue("AgentUuid") as string;
        if (Guid.TryParse(existing, out var id))
        {
            return id;
        }

        id = Guid.NewGuid();
        key.SetValue("AgentUuid", id.ToString("D"));
        return id;
    }

    public string GetHardwareHash()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath);
        if (key.GetValue("HardwareHash") is string cached && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var smbios = ReadSmBiosUuid() ?? Guid.NewGuid().ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(smbios)));
        key.SetValue("HardwareHash", hash);
        return hash;
    }

    public void SaveSecret(string secret)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            null,
            DataProtectionScope.LocalMachine);
        using var key = Registry.LocalMachine.CreateSubKey(RegPath);
        key.SetValue("AgentSecretProtected", Convert.ToBase64String(protectedBytes));
    }

    public string? LoadSecret()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegPath);
        if (key?.GetValue("AgentSecretProtected") is not string b64)
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unprotect agent secret");
            return null;
        }
    }

    public void SaveServerUrl(string serverUrl)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath);
        key.SetValue("ServerUrl", serverUrl);
    }

    public string GetServerUrl(string fallback) =>
        Registry.LocalMachine.OpenSubKey(RegPath)?.GetValue("ServerUrl") as string ?? fallback;

    private static string? ReadSmBiosUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject mo in searcher.Get())
            {
                return mo["UUID"]?.ToString();
            }
        }
        catch
        {
            // ignored — fall back to random
        }

        return null;
    }
}
