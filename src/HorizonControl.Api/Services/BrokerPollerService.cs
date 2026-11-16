using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HorizonControl.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HorizonControl.Api.Services;

/// <summary>
/// Polls Horizon Connection Server session APIs (or a lab mock that returns the same JSON shape).
/// Agents never call brokers — only the control plane does.
/// </summary>
public sealed class BrokerPollerService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<BrokerPollerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Broker poll cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var brokers = await db.HorizonBrokers.ToListAsync(cancellationToken);
        if (brokers.Count == 0)
        {
            return;
        }

        var client = httpClientFactory.CreateClient("horizon-broker");
        foreach (var broker in brokers)
        {
            try
            {
                var url = $"{broker.BaseUrl.TrimEnd('/')}/rest/monitor/v1/sessions";
                using var response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<HorizonSessionsResponse>(cancellationToken: cancellationToken);
                var sessions = payload?.Sessions ?? [];

                var old = db.BrokerSessionSnapshots.Where(s => s.BrokerId == broker.BrokerId);
                db.BrokerSessionSnapshots.RemoveRange(old);
                foreach (var s in sessions)
                {
                    db.BrokerSessionSnapshots.Add(new BrokerSessionSnapshot
                    {
                        SnapshotId = Guid.NewGuid(),
                        TenantId = broker.TenantId,
                        BrokerId = broker.BrokerId,
                        SessionId = s.Id ?? Guid.NewGuid().ToString("N"),
                        UserName = s.UserName,
                        DnsName = s.DnsName,
                        BrokerFqdn = broker.PublicFqdn,
                        ObservedAt = DateTimeOffset.UtcNow
                    });
                }

                broker.LastPollAt = DateTimeOffset.UtcNow;
                broker.LastPollError = null;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Broker {Name} polled {Count} sessions", broker.Name, sessions.Count);
            }
            catch (Exception ex)
            {
                broker.LastPollAt = DateTimeOffset.UtcNow;
                broker.LastPollError = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning(ex, "Broker poll failed for {Name}", broker.Name);
            }
        }
    }

    private sealed class HorizonSessionsResponse
    {
        [JsonPropertyName("sessions")]
        public List<HorizonSessionDto> Sessions { get; set; } = [];
    }

    private sealed class HorizonSessionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; set; }

        [JsonPropertyName("dns_name")]
        public string? DnsName { get; set; }
    }
}
