using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// Everything the product knows about one agent, and everything the Agents screen renders.
/// </summary>
/// <remarks>
/// <para>
/// This record is why adding an agent costs <b>one file and one registration line</b>: the screen
/// is generic and renders descriptors, so a new agent arrives with a name, a purpose, health,
/// history and a pause control without a line of UI being written.
/// </para>
/// <para>
/// The screen lists exactly the agents registered in this build, and never more (ADR-0014 D7).
/// A descriptor for an agent that does not run is the one thing this type must never be used for.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable, kebab-case, and <b>never renamed once shipped</b> — pause state, history and links are
/// keyed on it.
/// </param>
/// <param name="Name">What a person calls it. Title case, no "Worker" or "Service" suffix.</param>
/// <param name="Purpose">
/// <b>One hand-written sentence</b> saying what this agent does for the person reading it — not a
/// humanised class name. If it reads like the type name with spaces in it, it is not finished.
/// </param>
/// <param name="Kind">Watch, Decide, Act or Maintain.</param>
/// <param name="Authority">
/// What it may do on its own. Declared here and <b>enforced by the host</b>, not trusted.
/// </param>
/// <param name="Cadence">How often a cycle runs.</param>
/// <param name="Notes">
/// Optional. Where an agent behaves differently per cloud, say so here in plain words — this is
/// where capability honesty (rule R4) reaches the Agents screen.
/// </param>
/// <param name="May">Optional. What it may do, one plain line each — the screen's "may" list (unit 4.5).</param>
/// <param name="MayNot">Optional. What it never does, one plain line each. Say the limits; they are the safety story.</param>
/// <param name="LedgerActor">
/// Optional. The ledger actor identity this agent records its actions under (exact, or a prefix ending in ':'), so its
/// timeline can come from the ledger rather than a new table (unit 4.4). Null for an agent that acts as the person who
/// asked (Bulk Replay) or records nothing in the ledger.
/// </param>
public sealed record AgentDescriptor(
    string Id,
    string Name,
    string Purpose,
    AgentKind Kind,
    AgentAuthority Authority,
    TimeSpan Cadence,
    string? Notes = null,
    IReadOnlyList<string>? May = null,
    IReadOnlyList<string>? MayNot = null,
    string? LedgerActor = null)
{
    /// <summary>
    /// True when this agent can change something outside ServiceHub. The Agents screen leads with
    /// these, and there should be very few of them.
    /// </summary>
    public bool CanAct => Authority is AgentAuthority.ActsWithApproval or AgentAuthority.ActsAutonomously;
}
