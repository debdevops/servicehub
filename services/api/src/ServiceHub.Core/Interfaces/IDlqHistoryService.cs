using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Service for querying and managing DLQ message history.
/// Provides paginated queries, timeline views, summaries, and export capabilities.
/// </summary>
public interface IDlqHistoryService
{
    /// <summary>
    /// Gets paginated DLQ message history with optional filters.
    /// </summary>
    Task<Result<DlqHistoryPageResult>> GetHistoryAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? entityName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DlqMessageStatus? status = null,
        FailureCategory? category = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single DLQ message by ID with full details, scoped to the owner.
    /// Returns NotFound when the message belongs to a different owner.
    /// </summary>
    Task<Result<DlqMessage>> GetByIdAsync(string ownerId, long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the timeline (lifecycle events) for a specific DLQ message, scoped to the owner.
    /// Returns NotFound when the message belongs to a different owner.
    /// </summary>
    Task<Result<IReadOnlyList<DlqTimelineEvent>>> GetTimelineAsync(string ownerId, long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user notes on a DLQ message, scoped to the owner.
    /// Returns NotFound when the message belongs to a different owner.
    /// </summary>
    Task<Result<DlqMessage>> UpdateNotesAsync(string ownerId, long id, string notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triages a DLQ message by transitioning its lifecycle status, scoped to the owner.
    /// Only manual triage targets are accepted (Active/Archived/Discarded/Resolved); replay
    /// outcomes (Replayed/ReplayFailed) are set by the replay flow, not by triage. Stamps
    /// <see cref="DlqMessage.ArchivedAt"/> / <see cref="DlqMessage.ResolvedAt"/> as appropriate
    /// and optionally appends notes. Returns NotFound when the message belongs to a different owner.
    /// </summary>
    Task<Result<DlqMessage>> UpdateStatusAsync(
        string ownerId,
        long id,
        DlqMessageStatus newStatus,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of DLQ activity across all or a specific namespace, scoped to the owner.
    /// </summary>
    /// <param name="ownerId">Owner whose messages to summarise.</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="days">Number of days for the daily trend (default 30).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<DlqSummary>> GetSummaryAsync(string ownerId, Guid? namespaceId = null, int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports DLQ messages matching the given filters, scoped to the owner.
    /// </summary>
    Task<Result<IReadOnlyList<DlqMessage>>> ExportAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? entityName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DlqMessageStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the forensic analysis results on a DLQ message.
    /// </summary>
    Task<Result<DlqMessage>> UpdateForensicResultAsync(
        long id,
        FailureCategory category,
        double confidence,
        string rootCause,
        string replaySafety,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a DLQ history record by namespace, entity name, and sequence number.
    /// Returns the DLQ record ID if found.
    /// </summary>
    Task<Result<DlqMessage>> LookupAsync(
        Guid namespaceId,
        string entityName,
        long sequenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a tracked DLQ message for a recovery operation by transitioning it to a transient
    /// claim status (<see cref="DlqMessageStatus.Replaying"/> or <see cref="DlqMessageStatus.Purging"/>),
    /// using <see cref="DlqMessage.Status"/> as an EF Core concurrency token — the same
    /// claim-before-provider-call protocol <c>BulkOperationExecutor</c>, <c>SignatureReplayExecutor</c>
    /// and <c>AutoReplayExecutor</c> already use, exposed through the Core layer so API-layer
    /// callers (which may not depend on <c>DlqDbContext</c> directly) can participate in it too.
    /// Fails with a conflict if the message is not currently eligible (<c>Active</c> or
    /// <c>ReplayFailed</c>) or if another caller wins the claim race concurrently.
    /// </summary>
    /// <param name="id">The DLQ message's primary key.</param>
    /// <param name="dlqMessageOwnerId">
    /// The message row's own <see cref="DlqMessage.OwnerId"/> — <b>not necessarily the acting
    /// caller's owner ID</b>. For a shared namespace, the message's owner is the namespace owner,
    /// while the caller performing the claim may be a different, shared-with owner (see roadmap
    /// §14.8: ledger entries attribute to the acting owner, not the namespace owner).
    /// </param>
    /// <param name="claimStatus">The transient status to claim into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<DlqMessage>> ClaimForRecoveryAsync(
        long id,
        string dlqMessageOwnerId,
        DlqMessageStatus claimStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple DLQ messages by ID in one query, scoped to the owner. Missing or
    /// out-of-tenant IDs are silently omitted from the result rather than failing the call —
    /// used to resolve a failure signature's related messages, where some IDs may no longer
    /// be present.
    /// </summary>
    Task<Result<IReadOnlyList<DlqMessage>>> GetByIdsAsync(
        string ownerId,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Paginated result of DLQ history queries.
/// </summary>
public sealed record DlqHistoryPageResult(
    IReadOnlyList<DlqMessage> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>
/// A single event in the DLQ message timeline.
/// </summary>
public sealed record DlqTimelineEvent(
    string EventType,
    string Description,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string>? Details = null);

/// <summary>
/// Summary statistics for DLQ activity.
/// </summary>
public sealed record DlqSummary(
    int TotalMessages,
    int ActiveMessages,
    int ReplayedMessages,
    int ArchivedMessages,
    IReadOnlyDictionary<string, int> ByCategory,
    IReadOnlyDictionary<string, int> ByEntity,
    DateTimeOffset? OldestMessage,
    DateTimeOffset? NewestMessage,
    IReadOnlyList<DlqTrendPoint> DailyTrend);

/// <summary>
/// A data point in the DLQ trend over time.
/// </summary>
public sealed record DlqTrendPoint(
    DateTimeOffset Date,
    int NewMessages,
    int ResolvedMessages);
