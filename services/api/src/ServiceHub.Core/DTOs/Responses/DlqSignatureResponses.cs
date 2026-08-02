using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Operational knowledge attached to a failure signature.
/// Contains root cause, resolution notes, owner, and replay guidance.
/// </summary>
public sealed record KnowledgeResponse(
    string? RootCause,
    string? ResolutionNotes,
    string? OperationalNotes,
    string? RunbookLink,
    string? Owner,
    string? ReplayGuidance,
    DateTimeOffset? LastUpdatedAt,
    int KnowledgeVersion,
    DateTimeOffset? ReviewDueAt,
    string? Tags,
    string? UpdatedBy,
    bool IsReviewOverdue);

/// <summary>A prior version of a failure signature's operational knowledge.</summary>
public sealed record FailureKnowledgeHistoryResponse(
    int KnowledgeVersion,
    string? RootCause,
    string? ResolutionNotes,
    string? OperationalNotes,
    string? RunbookLink,
    string? Owner,
    string? ReplayGuidance,
    string? Tags,
    DateTimeOffset? ReviewDueAt,
    string? UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A namespace's DLQ error-signature analysis. <see cref="Available"/> is <see langword="false"/>
/// when the AI service could not be reached — the frontend renders its unavailable state from
/// this, it is never a non-200 response.
/// </summary>
public sealed record DlqSignaturesResponse(
    bool Available,
    string? Method,
    int BatchSize,
    IReadOnlyList<DlqClusterSignatureResponse> Clusters,
    IReadOnlyList<DlqSingletonSignatureResponse> Singletons);

/// <summary>One error-signature cluster, with message identity, history, explanation, and operational knowledge.</summary>
public sealed record DlqClusterSignatureResponse(
    int Size,
    IReadOnlyList<long> MessageIds,
    string DominantEntity,
    string DominantDeadletterReason,
    int DominantDeadletterReasonCount,
    IReadOnlyList<string> TopTerms,
    bool IsNew,
    DateTimeOffset FirstSeenAt,
    int OccurrenceCount,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Explanation,
    KnowledgeResponse? Knowledge,
    string SignatureHash,
    string Status,
    string Trend);

/// <summary>A message the AI service could not group into any cluster.</summary>
public sealed record DlqSingletonSignatureResponse(
    long MessageId,
    string DominantEntity,
    string DominantDeadletterReason);

/// <summary>
/// Full detail for a single failure signature: everything on <see cref="DlqClusterSignatureResponse"/>
/// plus its related messages and lifecycle confidence. Field-for-field superset of
/// <see cref="DlqClusterSignatureResponse"/> so the frontend's cluster-detail UI can render
/// either shape without a separate component.
/// </summary>
public sealed record DlqSignatureDetailResponse(
    string SignatureHash,
    Guid NamespaceId,
    int Size,
    IReadOnlyList<long> MessageIds,
    string DominantEntity,
    string DominantDeadletterReason,
    int DominantDeadletterReasonCount,
    IReadOnlyList<string> TopTerms,
    bool IsNew,
    DateTimeOffset FirstSeenAt,
    int OccurrenceCount,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Explanation,
    KnowledgeResponse? Knowledge,
    string Status,
    string Trend,
    string Confidence,
    bool IsCurrentlyClustered,
    IReadOnlyList<DlqHistoryResponse> RelatedMessages);

/// <summary>A failure signature's merged, computed timeline (first seen, recurrences, knowledge, lifecycle changes).</summary>
public sealed record SignatureTimelineResponse(
    string SignatureHash,
    IReadOnlyList<DlqTimelineEventResponse> Events);

/// <summary>Request to transition a failure signature's lifecycle status.</summary>
public sealed record UpdateSignatureStatusRequest(
    SignatureLifecycleStatus Status,
    [property: StringLength(4096)] string? Notes = null);

/// <summary>The result of a failure signature lifecycle transition.</summary>
public sealed record SignatureLifecycleStatusResponse(
    string SignatureHash,
    string Status,
    string? PreviousStatus,
    DateTimeOffset? TransitionedAt,
    string? Notes);
