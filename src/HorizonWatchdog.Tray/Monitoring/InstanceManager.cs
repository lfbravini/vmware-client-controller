using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Enums;
using HorizonControl.Shared.Paths;
using HorizonWatchdog.Tray.Native;

namespace HorizonWatchdog.Tray.Monitoring;

public sealed class TrackedInstance : IDisposable
{
    public Guid InstanceId { get; init; }
    public int RootPid { get; init; }
    public string JobObjectName { get; init; } = string.Empty;
    public string BrokerFqdn { get; set; } = "unknown";
    public string? HorizonUser { get; set; }
    public string? TargetApp { get; set; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string? CommandLine { get; init; }
    public SafeJobHandle Job { get; init; } = null!;
    public DateTimeOffset StartedAtUtc { get; init; }
    public ConcurrentDictionary<long, int> HangFailures { get; } = new();
    public ManagedInstanceState State { get; set; } = ManagedInstanceState.Healthy;

    public void Dispose() => Job.Dispose();
}

public sealed class InstanceManager
{
    private static readonly string[] ClientNames = ["vmware-view", "horizon-client", "HorizonWatchdog.FakeClient"];
    private readonly ConcurrentDictionary<Guid, TrackedInstance> _byId = new();
    private readonly ConcurrentDictionary<int, Guid> _pidToId = new();

    public void DiscoverAndTrack()
    {
        foreach (var name in ClientNames)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var proc in procs)
            {
                try
                {
                    if (_pidToId.ContainsKey(proc.Id))
                    {
                        continue;
                    }

                    AssignJob(proc);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to track PID {proc.Id}: {ex.Message}");
                }
            }
        }

        foreach (var (pid, id) in _pidToId.ToArray())
        {
            try
            {
                _ = Process.GetProcessById(pid);
            }
            catch
            {
                if (_byId.TryRemove(id, out var tracked))
                {
                    tracked.Dispose();
                }

                _pidToId.TryRemove(pid, out _);
            }
        }
    }

    private void AssignJob(Process proc)
    {
        DateTimeOffset started;
        try { started = proc.StartTime.ToUniversalTime(); }
        catch { started = DateTimeOffset.UtcNow; }

        var instanceId = StableInstanceId(Environment.MachineName, proc.ProcessName, proc.Id, started);
        var jobName = $"HorizonWatchdog_Job_{instanceId:N}";
        var job = JobObjectNative.CreateJobObject(IntPtr.Zero, jobName);
        if (job.IsInvalid)
        {
            throw new InvalidOperationException($"CreateJobObject failed: {Marshal.GetLastWin32Error()}");
        }

        var info = new JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JobObjectNative.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };
        if (!JobObjectNative.SetInformationJobObject(
                job,
                JobObjectNative.JobObjectExtendedLimitInformation,
                ref info,
                Marshal.SizeOf<JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            job.Dispose();
            throw new InvalidOperationException($"SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
        }

        var access = JobObjectNative.PROCESS_SET_QUOTA | JobObjectNative.PROCESS_TERMINATE |
                     JobObjectNative.PROCESS_QUERY_LIMITED_INFORMATION;
        var handle = JobObjectNative.OpenProcess(access, false, proc.Id);
        if (handle == IntPtr.Zero)
        {
            job.Dispose();
            throw new InvalidOperationException($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            if (!JobObjectNative.AssignProcessToJobObject(job, handle))
            {
                throw new InvalidOperationException($"AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            JobObjectNative.CloseHandle(handle);
        }

        string? exe = null;
        string? cmd = null;
        try { exe = proc.MainModule?.FileName; } catch { /* access denied */ }
        cmd = TryReadCommandLine(proc.Id);
        var (broker, user) = ParseBrokerAndUser(cmd, exe);

        var tracked = new TrackedInstance
        {
            InstanceId = instanceId,
            RootPid = proc.Id,
            JobObjectName = jobName,
            Job = job,
            StartedAtUtc = started,
            TargetApp = proc.ProcessName,
            ProcessName = proc.ProcessName,
            ExecutablePath = exe,
            CommandLine = cmd,
            BrokerFqdn = broker,
            HorizonUser = user
        };
        _byId[instanceId] = tracked;
        _pidToId[proc.Id] = instanceId;
        Debug.WriteLine($"Assigned PID {proc.Id} to {jobName} broker={broker}");
    }

    private static Guid StableInstanceId(string host, string processName, int pid, DateTimeOffset started)
    {
        var raw = $"{host}|{processName}|{pid}|{started:O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static string? TryReadCommandLine(int pid)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // WMI may be unavailable
        }

        return null;
    }

    private static (string Broker, string? User) ParseBrokerAndUser(string? commandLine, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            var name = Path.GetFileNameWithoutExtension(exePath ?? "") ?? "";
            return string.Equals(name, "HorizonWatchdog.FakeClient", StringComparison.OrdinalIgnoreCase)
                ? ("lab.fakeclient", Environment.UserName)
                : ("unknown", null);
        }

        // Common patterns: -serverURL https://broker.example -userName domain\user
        string? broker = null;
        string? user = null;
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim('"');
            if (p.Equals("-serverURL", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("--serverURL", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("-serverName", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < parts.Length)
                {
                    broker = parts[i + 1].Trim('"');
                    if (Uri.TryCreate(broker, UriKind.Absolute, out var uri))
                    {
                        broker = uri.Host;
                    }
                }
            }

            if (p.Equals("-userName", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("-loginAsCurrentUser", StringComparison.OrdinalIgnoreCase))
            {
                if (p.Equals("-loginAsCurrentUser", StringComparison.OrdinalIgnoreCase))
                {
                    user = Environment.UserName;
                }
                else if (i + 1 < parts.Length)
                {
                    user = parts[i + 1].Trim('"');
                }
            }
        }

        if (string.IsNullOrWhiteSpace(broker) &&
            commandLine.Contains("FakeClient", StringComparison.OrdinalIgnoreCase))
        {
            broker = "lab.fakeclient";
            user ??= Environment.UserName;
        }

        return (string.IsNullOrWhiteSpace(broker) ? "unknown" : broker, user);
    }

    public sealed record HangHit(TrackedInstance Instance, string Title, IntPtr Hwnd);

    public IReadOnlyList<HangHit> CheckHangs(int consecutiveRequired)
    {
        var degraded = new List<HangHit>();
        WindowNative.EnumWindows((hWnd, _) =>
        {
            if (!WindowNative.IsWindowVisible(hWnd))
            {
                return true;
            }

            WindowNative.GetWindowThreadProcessId(hWnd, out var pid);
            var tracked = FindByPid((int)pid);
            if (tracked is null)
            {
                return true;
            }

            var title = new StringBuilder(256);
            WindowNative.GetWindowText(hWnd, title, title.Capacity);
            var titleText = title.ToString();
            if (string.IsNullOrWhiteSpace(titleText))
            {
                return true;
            }

            var ok = WindowNative.SendMessageTimeout(
                hWnd,
                WindowNative.WM_NULL,
                IntPtr.Zero,
                IntPtr.Zero,
                WindowNative.SMTO_ABORTIFHUNG,
                2000,
                out _) != IntPtr.Zero;

            var key = hWnd.ToInt64();
            if (ok)
            {
                tracked.HangFailures[key] = 0;
                return true;
            }

            var failures = tracked.HangFailures.AddOrUpdate(key, 1, (_, v) => v + 1);
            if (failures >= consecutiveRequired)
            {
                tracked.State = ManagedInstanceState.AppHung;
                degraded.Add(new HangHit(tracked, titleText, hWnd));
            }

            return true;
        }, IntPtr.Zero);

        return degraded;
    }

    private TrackedInstance? FindByPid(int pid)
    {
        if (_pidToId.TryGetValue(pid, out var id) && _byId.TryGetValue(id, out var tracked))
        {
            return tracked;
        }

        foreach (var candidate in _byId.Values)
        {
            var ph = JobObjectNative.OpenProcess(JobObjectNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (ph == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                if (JobObjectNative.IsProcessInJob(ph, candidate.Job, out var inJob) && inJob)
                {
                    return candidate;
                }
            }
            finally
            {
                JobObjectNative.CloseHandle(ph);
            }
        }

        return null;
    }

    public bool TerminateInstance(Guid instanceId) =>
        TerminateInstance(instanceId, relaunch: false);

    public bool TerminateInstance(Guid instanceId, bool relaunch)
    {
        if (!_byId.TryGetValue(instanceId, out var tracked))
        {
            return false;
        }

        var processName = tracked.ProcessName;
        var exe = tracked.ExecutablePath;
        var broker = tracked.BrokerFqdn;
        var user = tracked.HorizonUser;

        if (!JobObjectNative.TerminateJobObject(tracked.Job, 1))
        {
            throw new InvalidOperationException($"TerminateJobObject failed: {Marshal.GetLastWin32Error()}");
        }

        tracked.State = ManagedInstanceState.Terminated;
        _byId.TryRemove(instanceId, out _);
        _pidToId.TryRemove(tracked.RootPid, out _);
        tracked.Dispose();
        Debug.WriteLine($"Terminated Job Object for instance {instanceId}");

        if (relaunch)
        {
            TryRelaunch(processName, exe, broker, user);
        }

        return true;
    }

    private static void TryRelaunch(string processName, string? exePath, string broker, string? user)
    {
        try
        {
            if (string.Equals(processName, "HorizonWatchdog.FakeClient", StringComparison.OrdinalIgnoreCase))
            {
                var path = exePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = Path.Combine(AppContext.BaseDirectory, "HorizonWatchdog.FakeClient.exe");
                }

                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }

                return;
            }

            // Horizon Client: never pass -password. Prefer -logInAsCurrentUser + serverURL when known.
            var horizon = exePath;
            if (string.IsNullOrWhiteSpace(horizon) || !File.Exists(horizon))
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VMware", "VMware Horizon View Client", "vmware-view.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Omnissa", "Omnissa Horizon Client", "horizon-client.exe")
                };
                horizon = candidates.FirstOrDefault(File.Exists);
            }

            if (string.IsNullOrWhiteSpace(horizon))
            {
                Debug.WriteLine("Relaunch skipped: Horizon Client executable not found");
                return;
            }

            var args = new StringBuilder("-logInAsCurrentUser");
            if (!string.IsNullOrWhiteSpace(broker) &&
                !string.Equals(broker, "unknown", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(broker, "lab.fakeclient", StringComparison.OrdinalIgnoreCase))
            {
                args.Append(" -serverURL ").Append(broker.Contains("://", StringComparison.Ordinal) ? broker : $"https://{broker}");
            }

            _ = user; // reserved for CredMgr lookup in a later slice
            Process.Start(new ProcessStartInfo(horizon, args.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Relaunch failed: {ex.Message}");
        }
    }

    public List<ManagedInstanceSnapshot> Snapshot() =>
        _byId.Values.Select(t => new ManagedInstanceSnapshot
        {
            InstanceId = t.InstanceId,
            BrokerFqdn = t.BrokerFqdn,
            HorizonUser = t.HorizonUser,
            TargetApp = t.TargetApp,
            RootPid = t.RootPid,
            JobObjectName = t.JobObjectName,
            State = t.State,
            StartedAtUtc = t.StartedAtUtc,
            SeamlessWindows = [],
            ChildProcesses = []
        }).ToList();

    public static IReadOnlyList<string> ExistingLogDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return HorizonLogPaths.GetLocalAppDataCandidates(local).Where(Directory.Exists).ToList();
    }
}
