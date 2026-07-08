namespace ServiceHub.Core.Events;

/// <summary>
/// Defines the top-level functional categories used to group <see cref="PlatformEvent"/> instances.
/// Categories correspond to the primary domain aggregate that produced the event.
/// Consumers may use categories to subscribe to a broad slice of events without
/// filtering on individual <see cref="EventTypes"/> strings.
/// </summary>
public static class EventCategories
{
    /// <summary>
    /// Events raised by namespace lifecycle operations
    /// (create, delete, connection test).
    /// </summary>
    public const string Namespace = "namespace";

    /// <summary>
    /// Events raised by dead-letter queue monitoring and detection.
    /// </summary>
    public const string Dlq = "dlq";

    /// <summary>
    /// Events raised when a DLQ message is replayed to its source queue or topic.
    /// </summary>
    public const string Replay = "replay";

    /// <summary>
    /// Events raised by the auto-replay rule engine when a rule matches or is modified.
    /// </summary>
    public const string Rule = "rule";
}
