using HorizonControl.Shared.Enums;

namespace HorizonControl.Shared.Commands;

/// <summary>
/// Envelope for control-plane → agent commands.
/// <para>
/// <b>TTL / clock skew:</b> Agents MUST drop commands when
/// <c>IssuedAtUtc + TimeSpan.FromSeconds(TtlSec)</c> is in the past (allow a small skew window if needed).
/// </para>
/// <para>
/// <b>Idempotency / ignore replay:</b> Agents MUST remember recent <see cref="CommandId"/> values.
/// A duplicate <see cref="CommandId"/> that was already executed (or intentionally skipped as expired)
/// MUST be acknowledged as success without re-executing the side effect.
/// </para>
/// Commands target <see cref="InstanceId"/> where applicable; PIDs are never the authority —
/// the Tray resolves the live Win32 Job Object handle locally.
/// </summary>
public sealed class AgentCommand
{
    public Guid CommandId { get; set; }

    public AgentCommandType Type { get; set; }

    /// <summary>Target managed instance when the command is instance-scoped.</summary>
    public Guid? InstanceId { get; set; }

    public int TtlSec { get; set; } = 120;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public static class AgentCommandExtensions
{
    public static bool IsExpired(this AgentCommand command, DateTimeOffset utcNow, TimeSpan clockSkewTolerance)
    {
        var deadline = command.IssuedAtUtc + TimeSpan.FromSeconds(command.TtlSec) + clockSkewTolerance;
        return utcNow > deadline;
    }
}
