using ServiceHub.Core.Enums;
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
    int PendingMatchCount,
    string? DisabledReason,
    string? DisabledReasonDetail,
    Guid? NamespaceId,
    RuleNamespaceScope NamespaceScope);

/// <summary>
/// Server-computed scope for a rule — the one object both the UI and rule execution trust,
/// resolved from <see cref="RuleResponse.NamespaceId"/> via <c>INamespaceRepository</c> rather
/// than inferred client-side from conditions.
/// </summary>
/// <param name="Kind"><c>"Global"</c> (matches every namespace), <c>"Namespace"</c> (scoped and
/// resolved), or <c>"Unresolved"</c> (scoped to a namespace that no longer exists/is accessible).</param>
/// <param name="Name">The namespace's display name, when <paramref name="Kind"/> is <c>"Namespace"</c>.</param>
/// <param name="Provider">The namespace's cloud provider, when <paramref name="Kind"/> is <c>"Namespace"</c>.</param>
/// <param name="Environment">The namespace's environment, when <paramref name="Kind"/> is <c>"Namespace"</c>.</param>
public sealed record RuleNamespaceScope(
    string Kind,
    string? Name = null,
    CloudProviderType? Provider = null,
    EnvironmentType? Environment = null)
{
    public static readonly RuleNamespaceScope Global = new("Global");
    public static readonly RuleNamespaceScope Unresolved = new("Unresolved");
}

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

/// <summary>
/// Response DTO for the intelligent auto-replay rule generation.
/// </summary>
public sealed record GenerateRulesResponse(
    int AnalysedMessages,
    int PatternsDetected,
    int RulesCreated,
    int RulesSkipped,
    IReadOnlyList<RuleResponse> Rules);
