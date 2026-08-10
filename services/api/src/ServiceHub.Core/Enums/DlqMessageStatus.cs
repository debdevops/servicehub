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
    ReplayFailed = 4,

    /// <summary>Message is no longer present in the DLQ (removed externally, expired, or consumed).</summary>
    Resolved = 5,

    /// <summary>
    /// Transient claim state: a replay worker has exclusively claimed this message (via
    /// optimistic concurrency) and is about to invoke the live provider. Guards against two
    /// workers (bulk replay, signature replay, auto-replay) sending the same message twice.
    /// </summary>
    Replaying = 6,

    /// <summary>
    /// Transient claim state: a bulk-purge worker has exclusively claimed this message (via
    /// optimistic concurrency) and is about to invoke the live provider. The purge counterpart
    /// of <see cref="Replaying"/> — without it, two concurrent purge jobs both issued a provider
    /// delete for the same message and the loser recorded a spurious failure.
    /// </summary>
    Purging = 7
}
