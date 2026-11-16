using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Commands;
using HorizonControl.Shared.Policy;
using HorizonWatchdog.Service.Identity;
using Microsoft.AspNetCore.SignalR.Client;

namespace HorizonWatchdog.Service.ControlPlane;

public sealed class ControlPlaneClient(
    AgentIdentityStore identity,
    ILogger<ControlPlaneClient> logger) : IAsyncDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private HubConnection? _hub;
    private Guid _agentUuid;
    private string? _secret;
    private string _baseUrl = "http://localhost:8080";

    public event Func<AgentCommand, Task>? CommandReceived;
    public event Func<OuPolicy, Task>? PolicyReceived;
    public OuPolicy CurrentPolicy { get; private set; } = new();

    public async Task EnsureEnrolledAsync(string enrollmentToken, CancellationToken cancellationToken)
    {
        _baseUrl = identity.GetServerUrl(Environment.GetEnvironmentVariable("HORIZON_SERVER") ?? "http://localhost:8080")
            .TrimEnd('/');
        _agentUuid = identity.GetOrCreateAgentUuid();
        _secret = identity.LoadSecret();

        if (_secret is null)
        {
            await EnrollAsync(enrollmentToken, rotate: true, cancellationToken);
        }
    }

    public async Task EnrollAsync(string enrollmentToken, bool rotate, CancellationToken cancellationToken)
    {
        _agentUuid = identity.GetOrCreateAgentUuid();
        var request = new AgentEnrollRequest
        {
            EnrollmentToken = enrollmentToken,
            AgentUuid = _agentUuid,
            Hostname = Environment.MachineName,
            HardwareHash = identity.GetHardwareHash(),
            IsDomainJoined = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("USERDOMAIN")) &&
                             !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase),
            OsVersion = Environment.OSVersion.ToString(),
            AgentVersion = "1.0.0-mvp",
            RotateSecret = rotate
        };

        using var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/v1/agent/enroll", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AgentEnrollResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("Empty enroll response.");
        _agentUuid = body.AgentUuid;
        _secret = body.AgentSecret;
        identity.SaveSecret(body.AgentSecret);
        CurrentPolicy = body.Policy;
        await CachePolicyAsync(body.Policy, cancellationToken);
        logger.LogInformation("Enrolled as agent {AgentUuid}", _agentUuid);
    }

    public async Task ConnectSignalRAsync(CancellationToken cancellationToken)
    {
        if (_secret is null)
        {
            throw new InvalidOperationException("Not enrolled.");
        }

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/agent", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>($"{_agentUuid:D}:{_secret}");
                options.Headers["Authorization"] = $"Bearer {_agentUuid:D}:{_secret}";
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<AgentCommand>("ReceiveCommand", async cmd =>
        {
            if (CommandReceived is not null)
            {
                await CommandReceived(cmd);
            }
        });

        _hub.On<OuPolicy>("PolicyUpdated", async policy =>
        {
            CurrentPolicy = policy;
            await CachePolicyAsync(policy, CancellationToken.None);
            if (PolicyReceived is not null)
            {
                await PolicyReceived(policy);
            }
        });

        await _hub.StartAsync(cancellationToken);
        await _hub.InvokeAsync("RegisterAsync", _agentUuid, cancellationToken);
        logger.LogInformation("SignalR connected to {BaseUrl}", _baseUrl);
    }

    public async Task SendHeartbeatAsync(AgentHeartbeatRequest heartbeat, CancellationToken cancellationToken)
    {
        heartbeat.AgentUuid = _agentUuid;
        if (_hub?.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync("HeartbeatAsync", heartbeat, cancellationToken);
            return;
        }

        // REST fallback
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/agent/heartbeat")
        {
            Content = JsonContent.Create(heartbeat)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{_agentUuid:D}:{_secret}");
        using var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportDegradedAsync(InstanceDegradedEvent degraded, CancellationToken cancellationToken)
    {
        degraded.AgentUuid = _agentUuid;
        if (_hub?.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync("ReportDegradedAsync", degraded, cancellationToken);
        }
    }

    public async Task AckCommandAsync(AgentCommandAck ack, CancellationToken cancellationToken)
    {
        if (_hub?.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync("AckCommandAsync", ack, cancellationToken);
        }
    }

    public Guid AgentUuid => _agentUuid;

    private static async Task CachePolicyAsync(OuPolicy policy, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HorizonWatchdog");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(policy), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }

        _http.Dispose();
    }
}
