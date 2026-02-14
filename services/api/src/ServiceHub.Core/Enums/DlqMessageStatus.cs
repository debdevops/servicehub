namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the lifecycle status of a dead-letter queue message.
/// Tracks the message from detection through resolution.
/// </summary>
public enum DlqMessageStatus
{
    /// <summary>Message detected in DLQ, awaiting action.</summary>
    Active = 0,

    /// <summary>Message has been replayed to the original or alternate entity.</summary>
    Replayed = 1,

    /// <summary>Message has been archived for future reference.</summary>
    Archived = 2,

    /// <summary>Message has been discarded/purged.</summary>
    Discarded = 3,

    /// <summary>Message replay was attempted but failed.</summary>
    ReplayFailed = 4
}
