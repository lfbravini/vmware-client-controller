using System.Text.Json;
using HorizonControl.Api.Data;
using HorizonControl.Api.Security;
using HorizonControl.Shared.Admin;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Enums;
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

        return group;
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

    private static async Task<IResult> ListInstancesAsync(FleetDbContext db)
    {
        var rows = await (
            from i in db.ManagedInstances.AsNoTracking()
            join e in db.Endpoints.AsNoTracking() on i.AgentUuid equals e.AgentUuid
            orderby i.UpdatedAt descending
            select new { i, e.Hostname }).ToListAsync();

        var items = rows.Select(r => new ManagedInstanceListItem
        {
            InstanceId = r.i.InstanceId,
            AgentUuid = r.i.AgentUuid,
            Hostname = r.Hostname,
            BrokerFqdn = r.i.BrokerFqdn,
            HorizonUser = r.i.HorizonUser,
            RootPid = r.i.RootPid,
            JobObjectName = r.i.JobObjectName,
            State = MapState(r.i.State),
            UpdatedAtUtc = r.i.UpdatedAt
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

        var command = new AgentCommand
        {
            CommandId = Guid.NewGuid(),
            Type = AgentCommandType.RestartClientInstance,
            InstanceId = instance.InstanceId,
            TtlSec = request.TtlSec <= 0 ? 120 : request.TtlSec,
            IssuedAtUtc = DateTimeOffset.UtcNow
        };

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
