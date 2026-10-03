namespace ServiceHub.Core.Models;

/// <summary>
/// What one agent cycle reports back to the host. Deliberately small: an agent says what it
/// examined and what it changed, and the host does everything else — timing, heartbeat, logging,
/// failure handling.
/// </summary>
/// <param name="Examined">How many items this cycle looked at. Zero is a normal, healthy answer.</param>
/// <param name="Changed">
/// How many items this cycle changed. <b>Must be zero for an agent whose authority is
/// <see cref="Enums.AgentAuthority.Observes"/> or <see cref="Enums.AgentAuthority.Proposes"/></b> —
/// the host treats a non-zero value there as a contract violation, not as a statistic.
/// </param>
/// <param name="Summary">
/// One short line for the agent's history, written for a person: "scanned 12 queues, 3 new dead
/// letters". Never an exception message.
/// </param>
/// <param name="Degraded">
/// True when the cycle completed but something was wrong — a provider call failed and was skipped,
/// a page was truncated. The agent is <see cref="Enums.AgentHealth.Degraded"/>, not failing, and
/// the reason belongs in <paramref name="Summary"/>.
/// </param>
public sealed record AgentCycleResult(
    int Examined,
    int Changed,
    string Summary,
    bool Degraded = false)
{
    /// <summary>A cycle that ran, found nothing to do, and is entirely healthy.</summary>
    public static AgentCycleResult Idle(string summary = "nothing to do") => new(0, 0, summary);
}
