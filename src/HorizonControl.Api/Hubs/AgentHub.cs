using System.Security.Claims;
using HorizonControl.Api.Data;
using HorizonControl.Api.Security;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HorizonControl.Api.Hubs;

[Authorize(AuthenticationSchemes = AgentAuthDefaults.Scheme)]
public sealed class AgentHub(FleetDbContext db, ILogger<AgentHub> logger) : Hub<IAgentHubClient>
{
    public override async Task OnConnectedAsync()
    {
        var agentUuid = GetAgentUuid();
        var tenantId = GetTenantId();
        await Groups.AddToGroupAsync(Context.ConnectionId, AgentHubGroups.ForAgent(tenantId, agentUuid));
        logger.LogInformation("Agent {AgentUuid} connected on {ConnectionId}", agentUuid, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public async Task RegisterAsync(Guid agentUuid)
    {
        if (agentUuid != GetAgentUuid())
        {
            throw new HubException("Agent UUID mismatch.");
        }

        await Task.CompletedTask;
    }

    public async Task HeartbeatAsync(AgentHeartbeatRequest heartbeat)
    {
        var agentUuid = GetAgentUuid();
        var tenantId = GetTenantId();
        if (heartbeat.AgentUuid != agentUuid)
        {
            throw new HubException("Agent UUID mismatch.");
        }

        TenantRlsScope.Set(tenantId);
        try
        {
            var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.AgentUuid == agentUuid);
            if (endpoint is null)
            {
                throw new HubException("Unknown agent.");
            }

            endpoint.LastSeenAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(heartbeat.Hostname))
            {
                endpoint.Hostname = heartbeat.Hostname;
            }

            if (!string.IsNullOrWhiteSpace(heartbeat.LocalIp))
            {
                endpoint.LocalIp = heartbeat.LocalIp;
            }

            if (!string.IsNullOrWhiteSpace(heartbeat.PublicIp))
            {
                endpoint.PublicIp = heartbeat.PublicIp;
            }

            if (!string.IsNullOrWhiteSpace(heartbeat.AgentVersion))
            {
                endpoint.AgentVersion = heartbeat.AgentVersion;
            }

            if (!string.IsNullOrWhiteSpace(heartbeat.HorizonClientVersion))
            {
                endpoint.HorizonClientVersion = heartbeat.HorizonClientVersion;
            }

            var seenIds = new HashSet<Guid>();
            foreach (var snap in heartbeat.Instances)
            {
                seenIds.Add(snap.InstanceId);
                var row = await db.ManagedInstances.FirstOrDefaultAsync(i => i.InstanceId == snap.InstanceId);
                if (row is null)
                {
                    row = new ManagedInstance
                    {
                        InstanceId = snap.InstanceId,
                        TenantId = tenantId,
                        AgentUuid = agentUuid,
                        StartedAt = snap.StartedAtUtc
                    };
                    db.ManagedInstances.Add(row);
                }

                row.BrokerFqdn = snap.BrokerFqdn;
                row.HorizonUser = snap.HorizonUser;
                row.RootPid = snap.RootPid;
                row.JobObjectName = snap.JobObjectName;
                row.State = snap.State.ToString().ToUpperInvariant() switch
                {
                    "HEALTHY" => "HEALTHY",
                    "APPHUNG" => "APP_HUNG",
                    "BLASTSTALLED" => "BLAST_STALLED",
                    "RECOVERING" => "RECOVERING",
                    "TERMINATED" => "TERMINATED",
                    _ => snap.State.ToString().ToUpperInvariant()
                };
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            var stale = await db.ManagedInstances
                .Where(i => i.AgentUuid == agentUuid && !seenIds.Contains(i.InstanceId) && i.State != "TERMINATED")
                .ToListAsync();
            foreach (var row in stale)
            {
                row.State = "TERMINATED";
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
        }
        finally
        {
            TenantRlsScope.Clear();
        }
    }

    public async Task ReportDegradedAsync(InstanceDegradedEvent degraded)
    {
        var agentUuid = GetAgentUuid();
        var tenantId = GetTenantId();
        TenantRlsScope.Set(tenantId);
        try
        {
            var row = await db.ManagedInstances.FirstOrDefaultAsync(i =>
                i.InstanceId == degraded.InstanceId && i.AgentUuid == agentUuid);
            if (row is not null)
            {
                row.State = "APP_HUNG";
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                ActorType = "AUTO_POLICY",
                ActorId = agentUuid.ToString("D"),
                Action = "INSTANCE_DEGRADED",
                TargetType = "INSTANCE",
                TargetId = degraded.InstanceId,
                MetadataJson = $"{{\"window\":\"{Escape(degraded.WindowTitle)}\",\"detail\":\"{Escape(degraded.Detail)}\"}}",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            logger.LogWarning(
                "Instance {InstanceId} degraded on agent {AgentUuid}: {WindowTitle}",
                degraded.InstanceId,
                agentUuid,
                degraded.WindowTitle);
        }
        finally
        {
            TenantRlsScope.Clear();
        }
    }

    public async Task AckCommandAsync(AgentCommandAck ack)
    {
        var agentUuid = GetAgentUuid();
        logger.LogInformation(
            "Command {CommandId} ack success={Success} replay={Replay} expired={Expired}",
            ack.CommandId,
            ack.Success,
            ack.IgnoredReplay,
            ack.Expired);

        if (ack.IgnoredReplay || ack.Expired)
        {
            return;
        }

        var tenantId = GetTenantId();
        TenantRlsScope.Set(tenantId);
        try
        {
            var row = await db.ProcessedCommands.FirstOrDefaultAsync(c => c.CommandId == ack.CommandId);
            if (row is null)
            {
                db.ProcessedCommands.Add(new ProcessedCommand
                {
                    CommandId = ack.CommandId,
                    AgentUuid = agentUuid,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Success = ack.Success,
                    Message = ack.Message
                });
            }
            else
            {
                row.Success = ack.Success;
                row.Message = ack.Message;
            }

            await db.SaveChangesAsync();
        }
        finally
        {
            TenantRlsScope.Clear();
        }
    }

    private Guid GetAgentUuid() =>
        Guid.Parse(Context.User?.FindFirstValue(AgentAuthDefaults.AgentUuidClaim)
                   ?? throw new HubException("Missing agent claim."));

    private Guid GetTenantId() =>
        Guid.Parse(Context.User?.FindFirstValue(AgentAuthDefaults.TenantIdClaim)
                   ?? throw new HubException("Missing tenant claim."));

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace('\\', ' ').Replace('"', '\'');
}

public static class TenantRlsScope
{
    public static void Set(Guid tenantId) =>
        Services.TenantRlsConnectionInterceptor.CurrentTenant.Value = tenantId;

    public static void Clear() =>
        Services.TenantRlsConnectionInterceptor.CurrentTenant.Value = null;
}
