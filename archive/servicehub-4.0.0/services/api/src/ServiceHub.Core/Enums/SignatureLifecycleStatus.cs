namespace ServiceHub.Core.Enums;

/// <summary>
/// The manual triage lifecycle of a failure signature (a recurring DLQ error cluster),
/// distinct from the always-Active <c>SignatureStatus</c> used by the per-message
/// fingerprinting pipeline. Tracked by <see cref="ServiceHub.Core.Interfaces.ISignatureLifecycleService"/>.
/// </summary>
public enum SignatureLifecycleStatus
{
    /// <summary>Default status: not yet triaged, or newly reopened work item.</summary>
    Active = 0,

    /// <summary>An engineer has resolved the underlying cause.</summary>
    Resolved = 1,

    /// <summary>Deliberately silenced — recognized but not being actively worked.</summary>
    Suppressed = 2,

    /// <summary>Terminal: closed out and no longer tracked operationally.</summary>
    Archived = 3,

    /// <summary>A previously Resolved or Suppressed signature has recurred and needs attention again.</summary>
    Reopened = 4,
}
