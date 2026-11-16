using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    public string BrokerFqdn { get; set; } = "lab.local";
    public string? HorizonUser { get; set; }
    public string? TargetApp { get; set; }
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
        var instanceId = Guid.NewGuid();
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

        var tracked = new TrackedInstance
        {
            InstanceId = instanceId,
            RootPid = proc.Id,
            JobObjectName = jobName,
            Job = job,
            StartedAtUtc = DateTimeOffset.UtcNow,
            TargetApp = proc.ProcessName
        };
        _byId[instanceId] = tracked;
        _pidToId[proc.Id] = instanceId;
        Debug.WriteLine($"Assigned PID {proc.Id} to {jobName}");
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

    public bool TerminateInstance(Guid instanceId)
    {
        if (!_byId.TryGetValue(instanceId, out var tracked))
        {
            return false;
        }

        if (!JobObjectNative.TerminateJobObject(tracked.Job, 1))
        {
            throw new InvalidOperationException($"TerminateJobObject failed: {Marshal.GetLastWin32Error()}");
        }

        tracked.State = ManagedInstanceState.Terminated;
        Debug.WriteLine($"Terminated Job Object for instance {instanceId}");
        return true;
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
