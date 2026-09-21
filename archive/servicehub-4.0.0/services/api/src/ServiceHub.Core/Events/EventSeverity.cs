namespace ServiceHub.Core.Events;

/// <summary>
/// Indicates the operational significance of a <see cref="PlatformEvent"/>.
/// Consumers may use this value to route events to different handlers,
/// alert channels, or analytics pipelines.
/// </summary>
public enum EventSeverity
{
    /// <summary>
    /// Routine informational event that requires no action.
    /// Examples: namespace created, replay completed successfully.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Anomalous condition that does not block operation but warrants attention.
    /// Examples: DLQ spike above threshold, rule matched on a high-volume queue.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// A significant failure occurred within a bounded scope.
    /// Examples: replay failed, forensic analysis returned an unsafe verdict.
    /// </summary>
    Error = 2,

    /// <summary>
    /// Severe condition requiring immediate operator intervention.
    /// Reserved for future use by the Alert Engine.
    /// </summary>
    Critical = 3
}
