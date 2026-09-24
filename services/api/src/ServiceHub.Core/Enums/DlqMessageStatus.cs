namespace ServiceHub.Core.Enums;

/// <summary>Where a dead-lettered message is in its life, as far as ServiceHub knows.</summary>
public enum DlqMessageStatus
{
    /// <summary>Seen in a dead-letter queue on the last scan that could confirm it.</summary>
    Active = 0,

    /// <summary>Replayed by ServiceHub.</summary>
    Replayed = 1,

    /// <summary>Archived — its namespace is no longer registered, so it can never be scanned or replayed.</summary>
    Archived = 2,

    /// <summary>Discarded on purpose.</summary>
    Discarded = 3,

    /// <summary>A replay was attempted and failed.</summary>
    ReplayFailed = 4,

    /// <summary>No longer in the dead-letter queue. See <see cref="DlqResolutionCause"/> for what is known about why.</summary>
    Resolved = 5,

    /// <summary>A replay is in flight. A scan must not touch a row in this state.</summary>
    Replaying = 6,

    /// <summary>A purge is in flight. A scan must not touch a row in this state.</summary>
    Purging = 7,
}
