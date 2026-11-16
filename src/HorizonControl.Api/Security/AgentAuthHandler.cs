using System.Security.Claims;
using System.Text.Encodings.Web;
using HorizonControl.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorizonControl.Api.Security;

public static class AgentAuthDefaults
{
    public const string Scheme = "AgentSecret";
    public const string AgentUuidClaim = "agent_uuid";
    public const string TenantIdClaim = "tenant_id";
}

public sealed class AgentAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FleetDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        if (Request.Headers.TryGetValue("Authorization", out var header))
        {
            var value = header.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = value["Bearer ".Length..].Trim();
            }
        }

        // SignalR AccessTokenProvider sends access_token on the query string.
        if (string.IsNullOrEmpty(token) && Request.Query.TryGetValue("access_token", out var accessToken))
        {
            token = accessToken.ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var parts = token.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var agentUuid) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return AuthenticateResult.Fail("Invalid agent bearer format.");
        }

        var endpoint = await db.Endpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.AgentUuid == agentUuid);
        if (endpoint is null)
        {
            return AuthenticateResult.Fail("Unknown agent.");
        }

        if (!SecretHasher.FixedTimeEqualsHash(parts[1], endpoint.AgentSecretHash))
        {
            return AuthenticateResult.Fail("Invalid agent secret.");
        }

        var claims = new[]
        {
            new Claim(AgentAuthDefaults.AgentUuidClaim, endpoint.AgentUuid.ToString("D")),
            new Claim(AgentAuthDefaults.TenantIdClaim, endpoint.TenantId.ToString("D")),
            new Claim(ClaimTypes.NameIdentifier, endpoint.AgentUuid.ToString("D"))
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
