using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for a single DLQ history entry.
/// </summary>
public sealed record DlqHistoryResponse(
    long Id,
    string MessageId,
    long SequenceNumber,
    string BodyHash,
    Guid NamespaceId,
    string EntityName,
    string EntityType,
    DateTimeOffset EnqueuedTimeUtc,
    DateTimeOffset? DeadLetterTimeUtc,
    DateTimeOffset DetectedAtUtc,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    int DeliveryCount,
    string? ContentType,
    long MessageSize,
    string? BodyPreview,
    string FailureCategory,
    double CategoryConfidence,
    string Status,
    DateTimeOffset? ReplayedAt,
    bool? ReplaySuccess,
    DateTimeOffset? ArchivedAt,
    string? UserNotes,
    string? CorrelationId,
    string? TopicName);

/// <summary>
/// Response DTO for a DLQ message with full details including replay history.
/// </summary>
public sealed record DlqMessageDetailResponse(
    long Id,
    string MessageId,
    long SequenceNumber,
    string BodyHash,
    Guid NamespaceId,
    string EntityName,
    string EntityType,
    DateTimeOffset EnqueuedTimeUtc,
    DateTimeOffset? DeadLetterTimeUtc,
    DateTimeOffset DetectedAtUtc,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    int DeliveryCount,
    string? ContentType,
    long MessageSize,
    string? BodyPreview,
    string? ApplicationPropertiesJson,
    string FailureCategory,
    double CategoryConfidence,
    string Status,
    DateTimeOffset? ReplayedAt,
    bool? ReplaySuccess,
    DateTimeOffset? ArchivedAt,
    string? UserNotes,
    string? CorrelationId,
    string? SessionId,
    string? TopicName,
    IReadOnlyList<ReplayHistoryResponse> ReplayHistory);

/// <summary>
/// Response DTO for a replay history entry.
/// </summary>
public sealed record ReplayHistoryResponse(
    long Id,
    DateTimeOffset ReplayedAt,
    string ReplayedBy,
    string ReplayStrategy,
    string ReplayedToEntity,
    string OutcomeStatus,
    string? NewDeadLetterReason,
    string? ErrorDetails);

/// <summary>
/// Response DTO for a timeline event.
/// </summary>
public sealed record DlqTimelineEventResponse(
    string EventType,
    string Description,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string>? Details);

/// <summary>
/// Response DTO for a DLQ timeline.
/// </summary>
public sealed record DlqTimelineResponse(
    long MessageId,
    string EntityName,
    IReadOnlyList<DlqTimelineEventResponse> Events);

/// <summary>
/// Response DTO for DLQ summary statistics.
/// </summary>
public sealed record DlqSummaryResponse(
    int TotalMessages,
    int ActiveMessages,
    int ReplayedMessages,
    int ArchivedMessages,
    IReadOnlyDictionary<string, int> ByCategory,
    IReadOnlyDictionary<string, int> ByEntity,
    DateTimeOffset? OldestMessage,
    DateTimeOffset? NewestMessage,
    IReadOnlyList<DlqTrendPointResponse> DailyTrend);

/// <summary>
/// Response DTO for a daily trend data point.
/// </summary>
public sealed record DlqTrendPointResponse(
    DateTimeOffset Date,
    int NewMessages,
    int ResolvedMessages);

/// <summary>
/// Request DTO for updating notes on a DLQ message.
/// </summary>
public sealed record UpdateDlqNotesRequest(string Notes);
