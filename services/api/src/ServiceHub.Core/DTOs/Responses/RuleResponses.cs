using ServiceHub.Core.Models;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for an auto-replay rule.
/// </summary>
public sealed record RuleResponse(
    long Id,
    string Name,
    string? Description,
    bool Enabled,
    IReadOnlyList<RuleCondition> Conditions,
    RuleAction Action,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long MatchCount,
    long SuccessCount,
    double SuccessRate,
    int MaxReplaysPerHour,
    int PendingMatchCount);

/// <summary>
/// Response DTO for rule test results.
/// </summary>
public sealed record RuleTestResponse(
    int TotalTested,
    int MatchedCount,
    double EstimatedSuccessRate,
    IReadOnlyList<RuleMatchResultResponse> SampleMatches);

/// <summary>
/// Response DTO for a single match result in a test.
/// </summary>
public sealed record RuleMatchResultResponse(
    long MessageId,
    string ServiceBusMessageId,
    string EntityName,
    bool IsMatch,
    string? MatchReason,
    string? DeadLetterReason);

/// <summary>
/// Response DTO for a "Replay All" bulk operation.
/// </summary>
public sealed record ReplayAllResponse(
    int TotalMatched,
    int Replayed,
    int Failed,
    int Skipped,
    IReadOnlyList<ReplayAllItemResponse> Results);

/// <summary>
/// Individual message result within a Replay All operation.
/// </summary>
public sealed record ReplayAllItemResponse(
    long DlqRecordId,
    string MessageId,
    string EntityName,
    string Outcome,
    string? Error);

/// <summary>
/// Response DTO for a rule template.
/// </summary>
public sealed record RuleTemplateResponse(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<RuleCondition> Conditions,
    RuleAction Action,
    int UsageCount,
    double Rating);
