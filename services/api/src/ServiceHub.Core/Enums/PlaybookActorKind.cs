namespace ServiceHub.Core.Enums;

/// <summary>
/// Who proposed or dispositioned a <see cref="Entities.PlaybookEntry"/> — deliberately a separate
/// enum from <c>RecoveryActorKind</c>, not a reuse of it: <see cref="ReasoningAgent"/> is a value
/// <c>RecoveryActorKind</c> must never gain (a reasoning agent must never be a valid Recovery
/// actor — see PLAYBOOK-LEDGER-DESIGN §9/§10), so keeping the two enums structurally distinct
/// enforces that boundary by construction rather than by convention.
/// </summary>
public enum PlaybookActorKind
{
    /// <summary>A deterministic detection worker (I3/P2/C1) or other system-internal process.</summary>
    System = 0,

    /// <summary>A human operator, identified by their resolved claims/SSO identity or API key name.</summary>
    User = 1,

    /// <summary>The future Tier 3 reasoning companion (<c>services/agent</c>). Not used until that
    /// service exists — its only legal write surface into persistence, per ADR-0005.</summary>
    ReasoningAgent = 2,
}
