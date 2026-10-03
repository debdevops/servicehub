namespace ServiceHub.Core.Enums;

/// <summary>
/// Lifecycle status of a <see cref="Entities.BulkOperationJob"/>.
/// </summary>
public enum BulkOperationStatus
{
    /// <summary>Job persisted and queued; the background worker has not started it yet.</summary>
    Pending = 0,

    /// <summary>The background worker is actively processing messages.</summary>
    Running = 1,

    /// <summary>Every matched message was processed successfully.</summary>
    Completed = 2,

    /// <summary>Processing finished, but one or more messages failed or were skipped.</summary>
    CompletedWithErrors = 3,

    /// <summary>The job stopped because of an unrecoverable error before it could finish (e.g. namespace deleted mid-run).</summary>
    Failed = 4,

    /// <summary>The job was cancelled by the caller before it finished processing every message.</summary>
    Cancelled = 5
}
