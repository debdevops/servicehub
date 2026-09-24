namespace ServiceHub.Core.Enums;

/// <summary>
/// Categorizes the root cause of a dead-letter queue message failure.
/// Used for heuristic classification and reporting.
/// </summary>
public enum FailureCategory
{
    /// <summary>Category has not been determined.</summary>
    Unknown = 0,

    /// <summary>Transient infrastructure error (timeout, connectivity).</summary>
    Transient = 1,

    /// <summary>Message exceeded maximum delivery count.</summary>
    MaxDelivery = 2,

    /// <summary>Message time-to-live expired.</summary>
    Expired = 3,

    /// <summary>Schema validation or data format error.</summary>
    DataQuality = 4,

    /// <summary>Authorization or permission failure.</summary>
    Authorization = 5,

    /// <summary>Application-level processing error.</summary>
    ProcessingError = 6,

    /// <summary>Resource not found during processing.</summary>
    ResourceNotFound = 7,

    /// <summary>Message size or quota exceeded.</summary>
    QuotaExceeded = 8
}
