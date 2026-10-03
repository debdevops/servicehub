namespace ServiceHub.Core.Enums;

/// <summary>
/// Why a message is <see cref="DlqMessageStatus.Resolved"/>. Absence from a scan proves only that the
/// message is gone — never who removed it — so a scan records <see cref="VanishedExternally"/>, and a
/// more specific cause is set only by something that actually saw the action (rule R5).
/// </summary>
public enum DlqResolutionCause
{
    /// <summary>ServiceHub replayed it.</summary>
    ReplayedByServiceHub = 0,

    /// <summary>ServiceHub purged it.</summary>
    PurgedByServiceHub = 1,

    /// <summary>It left the queue and ServiceHub did not see how — drained elsewhere, expired, or consumed by another tool.</summary>
    VanishedExternally = 2,

    /// <summary>A person declared it handled.</summary>
    DeclaredByOperator = 3,

    /// <summary>Unknown.</summary>
    Unknown = 4,
}
