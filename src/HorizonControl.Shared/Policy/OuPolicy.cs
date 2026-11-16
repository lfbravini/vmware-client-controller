using System.Text.Json.Serialization;
using HorizonControl.Shared.Enums;

namespace HorizonControl.Shared.Policy;

public sealed class OuPolicy
{
    [JsonPropertyName("policy_version")]
    public int PolicyVersion { get; set; } = 1;

    [JsonPropertyName("monitoring")]
    public MonitoringPolicy Monitoring { get; set; } = new();

    [JsonPropertyName("remediation")]
    public RemediationPolicy Remediation { get; set; } = new();

    [JsonPropertyName("multi_instance")]
    public MultiInstancePolicy MultiInstance { get; set; } = new();

    [JsonPropertyName("lifecycle")]
    public LifecyclePolicy Lifecycle { get; set; } = new();

    [JsonPropertyName("packages")]
    public PackagesPolicy Packages { get; set; } = new();
}

public sealed class MonitoringPolicy
{
    [JsonPropertyName("check_interval_sec")]
    public int CheckIntervalSec { get; set; } = 5;

    [JsonPropertyName("hang_threshold_sec")]
    public int HangThresholdSec { get; set; } = 20;

    [JsonPropertyName("consecutive_failures_required")]
    public int ConsecutiveFailuresRequired { get; set; } = 3;
}

public sealed class RemediationPolicy
{
    [JsonPropertyName("auto_remediate")]
    public bool AutoRemediate { get; set; } = true;

    [JsonPropertyName("max_restarts_per_hour")]
    public int MaxRestartsPerHour { get; set; } = 3;

    [JsonPropertyName("flush_cache_after_failed_restarts")]
    public int FlushCacheAfterFailedRestarts { get; set; } = 2;

    [JsonPropertyName("notify_user_on_restart")]
    public bool NotifyUserOnRestart { get; set; } = true;
}

public sealed class MultiInstancePolicy
{
    [JsonPropertyName("duplicate_same_broker_action")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DuplicateInstanceAction DuplicateSameBrokerAction { get; set; } = DuplicateInstanceAction.ReplaceIfHung;

    [JsonPropertyName("allow_multi_broker_connections")]
    public bool AllowMultiBrokerConnections { get; set; } = true;

    [JsonPropertyName("max_concurrent_client_instances")]
    public int MaxConcurrentClientInstances { get; set; } = 3;
}

public sealed class LifecyclePolicy
{
    [JsonPropertyName("pinned_apps")]
    public List<PinnedAppPolicy> PinnedApps { get; set; } = [];

    [JsonPropertyName("scheduled_reboot_cron")]
    public string? ScheduledRebootCron { get; set; }
}

public sealed class PinnedAppPolicy
{
    [JsonPropertyName("broker_url")]
    public string BrokerUrl { get; set; } = string.Empty;

    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("auto_launch")]
    public bool AutoLaunch { get; set; }
}

public sealed class PackagesPolicy
{
    [JsonPropertyName("desired_agent_version")]
    public string DesiredAgentVersion { get; set; } = "1.0.0";

    [JsonPropertyName("desired_rustdesk_version")]
    public string DesiredRustdeskVersion { get; set; } = "1.3.8";

    [JsonPropertyName("rustdesk_on_demand_only")]
    public bool RustdeskOnDemandOnly { get; set; } = true;
}
