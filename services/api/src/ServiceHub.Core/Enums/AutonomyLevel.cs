namespace ServiceHub.Core.Enums;

/// <summary>
/// How much unattended trust a failure signature has earned for one recovery action, per the
/// roadmap's earned-autonomy model (§8). L0–L2 are intelligence maturity (no execution occurs at
/// any of them, on any provider, ever); L3 is the permanent human-approved execution floor, not a
/// trust threshold to graduate past; L4–L5 are earned automation autonomy, the only levels where
/// <see cref="RecoveryActorKind.Automation"/> may execute without a human in the loop.
/// </summary>
public enum AutonomyLevel
{
    /// <summary>L0 — the failure has occurred and is recorded; nothing else.</summary>
    Observe = 0,

    /// <summary>L1 — the signature is named, classified, and its evidence is assembled.</summary>
    Explain = 1,

    /// <summary>L2 — a concrete recovery plan is generated and shown to a human; display only.</summary>
    Recommend = 2,

    /// <summary>L3 — a human approves each individual instance before it executes.</summary>
    Approve = 3,

    /// <summary>L4 — a pre-approved recipe executes without per-instance human approval, budget-bounded.</summary>
    Standing = 4,

    /// <summary>L5 — identical execution path to L4; differs only in accumulated evidence and demotion sensitivity.</summary>
    Unattended = 5
}
