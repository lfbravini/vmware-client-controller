using System.Security.Claims;
using System.Text.Json;
using HorizonControl.Api.Data;
using HorizonControl.Api.Hubs;
using HorizonControl.Api.Security;
using HorizonControl.Api.Services;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HorizonControl.Api.Endpoints;

public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agent");
        group.MapPost("/enroll", EnrollAsync);
        group.MapPost("/heartbeat", HeartbeatAsync).RequireAuthorization(AgentAuthDefaults.Scheme);
        return group;
    }

    private static async Task<IResult> EnrollAsync(
        [FromBody] AgentEnrollRequest request,
        FleetDbContext db,
        EnrollRateLimiter rateLimiter,
        HttpContext http,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AgentEnroll");
        var clientKey = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(clientKey))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (string.IsNullOrWhiteSpace(request.EnrollmentToken) ||
            string.IsNullOrWhiteSpace(request.HardwareHash) ||
            string.IsNullOrWhiteSpace(request.Hostname))
        {
            return Results.BadRequest(new { error = "token, hardwareHash, and hostname are required." });
        }

        var tokenHash = SecretHasher.Hash(request.EnrollmentToken);
        var token = await db.EnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (token is null)
        {
            return Results.Unauthorized();
        }

        if (token.ExpiresAt is not null && token.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.Unauthorized();
        }

        if (token.MaxUses is not null && token.UseCount >= token.MaxUses)
        {
            return Results.Unauthorized();
        }

        var existing = await db.Endpoints.FirstOrDefaultAsync(e =>
            e.TenantId == token.TenantId && e.HardwareHash == request.HardwareHash);

        var secret = SecretHasher.CreateAgentSecret();
        var secretHash = SecretHasher.Hash(secret);
        Guid agentUuid;

        if (existing is null)
        {
            agentUuid = request.AgentUuid is { } id && id != Guid.Empty ? id : Guid.NewGuid();
            existing = new FleetEndpoint
            {
                AgentUuid = agentUuid,
                TenantId = token.TenantId,
                OuId = token.OuId,
                Hostname = request.Hostname,
                HardwareHash = request.HardwareHash,
                AgentSecretHash = secretHash,
                IsDomainJoined = request.IsDomainJoined,
                AdOuDn = request.AdOuDn,
                OsVersion = request.OsVersion,
                AgentVersion = request.AgentVersion,
                HorizonClientVersion = request.HorizonClientVersion,
                LocalIp = request.LocalIp,
                MacAddress = request.MacAddress,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            db.Endpoints.Add(existing);
        }
        else
        {
            // Same hardware hash → issue new secret and invalidate the previous hash.
            agentUuid = existing.AgentUuid;
            existing.AgentSecretHash = secretHash;
            existing.Hostname = request.Hostname;
            existing.OuId = token.OuId;
            existing.IsDomainJoined = request.IsDomainJoined;
            existing.AdOuDn = request.AdOuDn;
            existing.OsVersion = request.OsVersion;
            existing.AgentVersion = request.AgentVersion;
            existing.HorizonClientVersion = request.HorizonClientVersion;
            existing.LocalIp = request.LocalIp;
            existing.MacAddress = request.MacAddress;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
        }

        token.UseCount++;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = token.TenantId,
            ActorType = "AGENT",
            ActorId = agentUuid.ToString("D"),
            Action = "ENROLL",
            TargetType = "ENDPOINT",
            TargetId = agentUuid,
            MetadataJson = "{\"rotated\":true}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var ou = await db.VirtualOus.AsNoTracking().FirstAsync(o => o.OuId == token.OuId);
        var policy = JsonSerializer.Deserialize<OuPolicy>(ou.PolicyOverrideJson) ?? new OuPolicy();

        logger.LogInformation("Enrolled agent {AgentUuid} for tenant {TenantId}", agentUuid, token.TenantId);

        return Results.Ok(new AgentEnrollResponse
        {
            AgentUuid = agentUuid,
            TenantId = token.TenantId,
            OuId = token.OuId,
            AgentSecret = secret,
            Policy = policy
        });
    }

    private static async Task<IResult> HeartbeatAsync(
        [FromBody] AgentHeartbeatRequest request,
        FleetDbContext db,
        ClaimsPrincipal principal)
    {
        var agentUuid = Guid.Parse(principal.FindFirstValue(AgentAuthDefaults.AgentUuidClaim)!);
        var tenantId = Guid.Parse(principal.FindFirstValue(AgentAuthDefaults.TenantIdClaim)!);
        if (request.AgentUuid != agentUuid)
        {
            return Results.Forbid();
        }

        TenantRlsScope.Set(tenantId);
        try
        {
            var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.AgentUuid == agentUuid);
            if (endpoint is null)
            {
                return Results.NotFound();
            }

            endpoint.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }
        finally
        {
            TenantRlsScope.Clear();
        }
    }
}
