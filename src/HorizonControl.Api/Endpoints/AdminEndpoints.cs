using System.Text.Json;
using HorizonControl.Api.Data;
using HorizonControl.Api.Security;
using HorizonControl.Shared.Admin;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;
using HorizonControl.Shared.Policy;
using HorizonControl.Shared.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HorizonControl.Api.Hubs;

namespace HorizonControl.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").AddEndpointFilter(new AdminApiKeyEndpointFilter());

        group.MapGet("/endpoints", ListEndpointsAsync);
        group.MapGet("/instances", ListInstancesAsync);
        group.MapPost("/instances/restart", RestartInstanceAsync);
        group.MapGet("/tenants", ListTenantsAsync);
        group.MapGet("/ous", ListOusAsync);
        group.MapGet("/enrollment-tokens", ListEnrollmentTokensAsync);
        group.MapPost("/enrollment-tokens", CreateEnrollmentTokenAsync);
        group.MapGet("/ous/{ouId:guid}/policy", GetOuPolicyAsync);
        group.MapPut("/ous/{ouId:guid}/policy", PutOuPolicyAsync);
        group.MapGet("/commands", ListCommandsAsync);
        group.MapGet("/brokers", ListBrokersAsync);
        group.MapPost("/brokers", UpsertBrokerAsync);
        group.MapGet("/broker-sessions", ListBrokerSessionsAsync);

        return group;
    }

    private static async Task<IResult> GetOuPolicyAsync(Guid ouId, FleetDbContext db)
    {
        var ou = await db.VirtualOus.AsNoTracking().FirstOrDefaultAsync(o => o.OuId == ouId);
        if (ou is null)
        {
            return Results.NotFound();
        }

        var policy = string.IsNullOrWhiteSpace(ou.PolicyOverrideJson)
            ? new OuPolicy()
            : JsonSerializer.Deserialize<OuPolicy>(ou.PolicyOverrideJson) ?? new OuPolicy();
        return Results.Ok(policy);
    }

    private static async Task<IResult> PutOuPolicyAsync(
        Guid ouId,
        [FromBody] OuPolicy policy,
        FleetDbContext db,
        IHubContext<AgentHub, IAgentHubClient> hub)
    {
        var ou = await db.VirtualOus.FirstOrDefaultAsync(o => o.OuId == ouId);
        if (ou is null)
        {
            return Results.NotFound();
        }

        ou.PolicyOverrideJson = JsonSerializer.Serialize(policy);
        await db.SaveChangesAsync();

        var agentIds = await db.Endpoints.AsNoTracking()
            .Where(e => e.OuId == ouId)
            .Select(e => e.AgentUuid)
            .ToListAsync();
        foreach (var agentUuid in agentIds)
        {
            await hub.Clients.Group(AgentHubGroups.ForAgent(ou.TenantId, agentUuid)).PolicyUpdated(policy);
        }

        return Results.Ok(policy);
    }

    private static async Task<IResult> ListCommandsAsync(FleetDbContext db, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var items = await db.ProcessedCommands.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new CommandListItem
            {
                CommandId = c.CommandId,
                AgentUuid = c.AgentUuid,
                InstanceId = c.InstanceId,
                Success = c.Success,
                Message = c.Message,
                CreatedAtUtc = c.CreatedAt
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> ListBrokersAsync(FleetDbContext db, [FromQuery] Guid? tenantId)
    {
        var q = db.HorizonBrokers.AsNoTracking().AsQueryable();
        if (tenantId is { } tid)
        {
            q = q.Where(b => b.TenantId == tid);
        }

        var items = await q.OrderBy(b => b.Name)
            .Select(b => new HorizonBrokerListItem
            {
                BrokerId = b.BrokerId,
                TenantId = b.TenantId,
                Name = b.Name,
                BaseUrl = b.BaseUrl,
                PublicFqdn = b.PublicFqdn,
                PollIntervalSec = b.PollIntervalSec,
                LastPollAtUtc = b.LastPollAt,
                LastPollError = b.LastPollError
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> UpsertBrokerAsync([FromBody] UpsertHorizonBrokerRequest request, FleetDbContext db)
    {
        if (!await db.Tenants.AnyAsync(t => t.TenantId == request.TenantId))
        {
            return Results.BadRequest(new { error = "Unknown tenant." });
        }

        var fqdn = string.IsNullOrWhiteSpace(request.PublicFqdn)
            ? (Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var u) ? u.Host : request.BaseUrl)
            : request.PublicFqdn;

        var existing = await db.HorizonBrokers.FirstOrDefaultAsync(b =>
            b.TenantId == request.TenantId && b.PublicFqdn == fqdn);
        if (existing is null)
        {
            existing = new HorizonBroker
            {
                BrokerId = Guid.NewGuid(),
                TenantId = request.TenantId,
                PublicFqdn = fqdn
            };
            db.HorizonBrokers.Add(existing);
        }

        existing.Name = request.Name;
        existing.BaseUrl = request.BaseUrl.TrimEnd('/');
        existing.PublicFqdn = fqdn;
        existing.EncryptedCredentials = request.Credentials ?? existing.EncryptedCredentials;
        existing.PollIntervalSec = request.PollIntervalSec <= 0 ? 30 : request.PollIntervalSec;
        await db.SaveChangesAsync();

        return Results.Ok(new HorizonBrokerListItem
        {
            BrokerId = existing.BrokerId,
            TenantId = existing.TenantId,
            Name = existing.Name,
            BaseUrl = existing.BaseUrl,
            PublicFqdn = existing.PublicFqdn,
            PollIntervalSec = existing.PollIntervalSec,
            LastPollAtUtc = existing.LastPollAt,
            LastPollError = existing.LastPollError
        });
    }

    private static async Task<IResult> ListBrokerSessionsAsync(FleetDbContext db)
    {
        var sessions = await db.BrokerSessionSnapshots.AsNoTracking()
            .OrderByDescending(s => s.ObservedAt)
            .Take(200)
            .ToListAsync();
        var instances = await db.ManagedInstances.AsNoTracking()
            .Where(i => i.State != "TERMINATED")
            .ToListAsync();
        var endpoints = await db.Endpoints.AsNoTracking().ToDictionaryAsync(e => e.AgentUuid, e => e.Hostname);

        var items = sessions.Select(s =>
        {
            var match = instances.FirstOrDefault(i =>
                string.Equals(i.BrokerFqdn, s.BrokerFqdn, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(s.UserName) ||
                 string.Equals(i.HorizonUser, s.UserName, StringComparison.OrdinalIgnoreCase)));
            return new BrokerSessionListItem
            {
                SnapshotId = s.SnapshotId,
                BrokerId = s.BrokerId,
                SessionId = s.SessionId,
                UserName = s.UserName,
                DnsName = s.DnsName,
                BrokerFqdn = s.BrokerFqdn,
                ObservedAtUtc = s.ObservedAt,
                CorrelatedInstanceId = match?.InstanceId,
                CorrelatedHostname = match is null ? null : endpoints.GetValueOrDefault(match.AgentUuid)
            };
        }).ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult> ListTenantsAsync(FleetDbContext db)
    {
        var items = await db.Tenants.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TenantListItem
            {
                TenantId = t.TenantId,
                Name = t.Name,
                Slug = t.Slug,
                Status = t.Status,
                CreatedAtUtc = t.CreatedAt
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> ListOusAsync(FleetDbContext db, [FromQuery] Guid? tenantId)
    {
        var query = db.VirtualOus.AsNoTracking().AsQueryable();
        if (tenantId is { } tid)
        {
            query = query.Where(o => o.TenantId == tid);
        }

        var items = await query
            .OrderBy(o => o.Path)
            .Select(o => new VirtualOuListItem
            {
                OuId = o.OuId,
                TenantId = o.TenantId,
                Name = o.Name,
                Path = o.Path
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> ListEnrollmentTokensAsync(FleetDbContext db, [FromQuery] Guid? tenantId)
    {
        var query = db.EnrollmentTokens.AsNoTracking().AsQueryable();
        if (tenantId is { } tid)
        {
            query = query.Where(t => t.TenantId == tid);
        }

        var items = await query
            .OrderByDescending(t => t.TokenId)
            .Select(t => new EnrollmentTokenListItem
            {
                TokenId = t.TokenId,
                TenantId = t.TenantId,
                OuId = t.OuId,
                ExpiresAtUtc = t.ExpiresAt,
                MaxUses = t.MaxUses,
                UseCount = t.UseCount
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateEnrollmentTokenAsync(
        [FromBody] CreateEnrollmentTokenRequest request,
        FleetDbContext db)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == request.TenantId);
        if (tenant is null)
        {
            return Results.BadRequest(new { error = "Unknown tenant." });
        }

        var ou = await db.VirtualOus.FirstOrDefaultAsync(o =>
            o.OuId == request.OuId && o.TenantId == request.TenantId);
        if (ou is null)
        {
            return Results.BadRequest(new { error = "Unknown OU for tenant." });
        }

        var plain = string.IsNullOrWhiteSpace(request.PlainToken)
            ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            : request.PlainToken.Trim();
        var hash = SecretHasher.Hash(plain);
        if (await db.EnrollmentTokens.AnyAsync(t => t.TokenHash == hash))
        {
            return Results.Conflict(new { error = "Token hash already exists." });
        }

        var row = new EnrollmentToken
        {
            TokenId = Guid.NewGuid(),
            TenantId = request.TenantId,
            OuId = request.OuId,
            TokenHash = hash,
            ExpiresAt = request.ExpiresAtUtc,
            MaxUses = request.MaxUses,
            UseCount = 0
        };
        db.EnrollmentTokens.Add(row);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = request.TenantId,
            ActorType = "ADMIN_USER",
            ActorId = "admin-api-key",
            Action = "CREATE_ENROLLMENT_TOKEN",
            TargetType = "ENROLLMENT_TOKEN",
            TargetId = row.TokenId,
            MetadataJson = JsonSerializer.Serialize(new { row.OuId, row.MaxUses }),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return Results.Ok(new CreateEnrollmentTokenResponse
        {
            TokenId = row.TokenId,
            TenantId = row.TenantId,
            OuId = row.OuId,
            PlainToken = plain,
            ExpiresAtUtc = row.ExpiresAt,
            MaxUses = row.MaxUses
        });
    }

    private static async Task<IResult> ListEndpointsAsync(FleetDbContext db)
    {
        var items = await db.Endpoints.AsNoTracking()
            .OrderByDescending(e => e.LastSeenAt)
            .Select(e => new EndpointListItem
            {
                AgentUuid = e.AgentUuid,
                TenantId = e.TenantId,
                OuId = e.OuId,
                Hostname = e.Hostname,
                IsDomainJoined = e.IsDomainJoined,
                AgentVersion = e.AgentVersion,
                LastSeenAtUtc = e.LastSeenAt
            })
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> ListInstancesAsync(FleetDbContext db, [FromQuery] bool liveOnly = false)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);
        var rows = await (
            from i in db.ManagedInstances.AsNoTracking()
            join e in db.Endpoints.AsNoTracking() on i.AgentUuid equals e.AgentUuid
            orderby i.UpdatedAt descending
            select new { i, e.Hostname, e.LastSeenAt }).ToListAsync();

        var items = rows
            .Where(r => !liveOnly || (r.i.State != "TERMINATED" && r.i.UpdatedAt >= cutoff))
            .Select(r => new ManagedInstanceListItem
            {
                InstanceId = r.i.InstanceId,
                AgentUuid = r.i.AgentUuid,
                Hostname = r.Hostname,
                BrokerFqdn = r.i.BrokerFqdn,
                HorizonUser = r.i.HorizonUser,
                RootPid = r.i.RootPid,
                JobObjectName = r.i.JobObjectName,
                State = MapState(r.i.State),
                UpdatedAtUtc = r.i.UpdatedAt,
                IsLive = r.i.State != "TERMINATED" && r.i.UpdatedAt >= cutoff && r.LastSeenAt >= cutoff
            }).ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult> RestartInstanceAsync(
        [FromBody] RestartInstanceRequest request,
        FleetDbContext db,
        IHubContext<AgentHub, IAgentHubClient> hub)
    {
        var instance = await db.ManagedInstances.FirstOrDefaultAsync(i => i.InstanceId == request.InstanceId);
        if (instance is null)
        {
            return Results.NotFound(new { error = "Instance not found." });
        }

        if (string.Equals(instance.State, "TERMINATED", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Instance is TERMINATED; cannot restart." });
        }

        var endpoint = await db.Endpoints.AsNoTracking().FirstOrDefaultAsync(e => e.AgentUuid == instance.AgentUuid);
        if (endpoint is null || endpoint.LastSeenAt < DateTimeOffset.UtcNow.AddMinutes(-2))
        {
            return Results.BadRequest(new { error = "Agent appears offline (lastSeen stale)." });
        }

        var command = new AgentCommand
        {
            CommandId = Guid.NewGuid(),
            Type = AgentCommandType.RestartClientInstance,
            InstanceId = instance.InstanceId,
            TtlSec = request.TtlSec <= 0 ? 120 : request.TtlSec,
            IssuedAtUtc = DateTimeOffset.UtcNow
        };

        db.ProcessedCommands.Add(new ProcessedCommand
        {
            CommandId = command.CommandId,
            AgentUuid = instance.AgentUuid,
            InstanceId = instance.InstanceId,
            CreatedAt = DateTimeOffset.UtcNow,
            Success = false,
            Message = "Dispatched"
        });
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = instance.TenantId,
            ActorType = "ADMIN_USER",
            ActorId = "admin-api-key",
            Action = "RESTART_INSTANCE",
            TargetType = "INSTANCE",
            TargetId = instance.InstanceId,
            MetadataJson = JsonSerializer.Serialize(new { command.CommandId }),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await hub.Clients.Group(AgentHubGroups.ForAgent(instance.TenantId, instance.AgentUuid))
            .ReceiveCommand(command);

        return Results.Ok(new RestartInstanceResponse
        {
            CommandId = command.CommandId,
            InstanceId = instance.InstanceId,
            AgentUuid = instance.AgentUuid
        });
    }

    private static ManagedInstanceState MapState(string state) =>
        state.ToUpperInvariant() switch
        {
            "APP_HUNG" or "APPHUNG" => ManagedInstanceState.AppHung,
            "BLAST_STALLED" or "BLASTSTALLED" => ManagedInstanceState.BlastStalled,
            "RECOVERING" => ManagedInstanceState.Recovering,
            "TERMINATED" => ManagedInstanceState.Terminated,
            _ => ManagedInstanceState.Healthy
        };
}

public sealed class AdminApiKeyEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Options.FleetControlOptions>>().Value;
        if (!http.Request.Headers.TryGetValue(AdminApiKeyAttribute.HeaderName, out var provided) ||
            !string.Equals(provided.ToString(), options.AdminApiKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
