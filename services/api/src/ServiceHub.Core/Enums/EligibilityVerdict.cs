namespace ServiceHub.Core.Enums;

/// <summary>
/// The <see cref="Interfaces.IRecoveryEligibilityGate"/>'s decision for one recovery attempt.
/// </summary>
public enum EligibilityVerdict
{
    /// <summary>No predicate blocked the attempt — proceed.</summary>
    Allow = 0,

    /// <summary>A predicate requires manual review before this attempt proceeds.</summary>
    Escalate = 1,

    /// <summary>A predicate permanently forecloses this attempt — non-overridable.</summary>
    Deny = 2
}
