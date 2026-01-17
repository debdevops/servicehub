namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the state of a message in Azure Service Bus.
/// </summary>
public enum MessageState
{
    /// <summary>
    /// The message is active and available for processing.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The message has been deferred for later processing.
    /// </summary>
    Deferred = 1,

    /// <summary>
    /// The message has been scheduled for future delivery.
    /// </summary>
    Scheduled = 2,

    /// <summary>
    /// The message is in the dead-letter queue due to processing failures.
    /// </summary>
    DeadLettered = 3,

    /// <summary>
    /// The message has been completed/acknowledged successfully.
    /// </summary>
    Completed = 4,

    /// <summary>
    /// The message has been abandoned and returned to the queue.
    /// </summary>
    Abandoned = 5
}
