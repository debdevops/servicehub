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
    /// Gets a single DLQ message by ID with full details.
    /// </summary>
    Task<Result<DlqMessage>> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the timeline (lifecycle events) for a specific DLQ message.
    /// </summary>
    Task<Result<IReadOnlyList<DlqTimelineEvent>>> GetTimelineAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user notes on a DLQ message.
    /// </summary>
    Task<Result<DlqMessage>> UpdateNotesAsync(long id, string notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of DLQ activity across all or a specific namespace.
    /// </summary>
    Task<Result<DlqSummary>> GetSummaryAsync(Guid? namespaceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports DLQ messages matching the given filters.
    /// </summary>
    Task<Result<IReadOnlyList<DlqMessage>>> ExportAsync(
        Guid? namespaceId = null,
        string? entityName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DlqMessageStatus? status = null,
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
