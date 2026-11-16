using HorizonControl.Shared.Agent;
using HorizonControl.Shared.Commands;

namespace HorizonControl.Shared.SignalR;

/// <summary>Methods the agent invokes on the control plane hub.</summary>
public interface IAgentHubServer
{
    Task RegisterAsync(Guid agentUuid, CancellationToken cancellationToken = default);

    Task HeartbeatAsync(AgentHeartbeatRequest heartbeat, CancellationToken cancellationToken = default);

    Task ReportDegradedAsync(InstanceDegradedEvent degraded, CancellationToken cancellationToken = default);

    Task AckCommandAsync(AgentCommandAck ack, CancellationToken cancellationToken = default);
}

/// <summary>Methods the control plane pushes to a connected agent.</summary>
public interface IAgentHubClient
{
    Task ReceiveCommand(AgentCommand command);

    Task PolicyUpdated(Policy.OuPolicy policy);
}

public static class AgentHubGroups
{
    public static string ForAgent(Guid tenantId, Guid agentUuid) =>
        $"tenant:{tenantId:D}:agent:{agentUuid:D}";

    public static string ForTenant(Guid tenantId) =>
        $"tenant:{tenantId:D}";
}
