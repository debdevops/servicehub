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
    [StringLength(4096)] string? Notes = null);

/// <summary>The result of a failure signature lifecycle transition.</summary>
public sealed record SignatureLifecycleStatusResponse(
    string SignatureHash,
    string Status,
    string? PreviousStatus,
    DateTimeOffset? TransitionedAt,
    string? Notes);

/// <summary>
/// The most recent signature-replay job's outcome for one namespace, if the signature has ever
/// been replayed there — a single already-durable fact (job status + when it was created), never
/// a computed safety score.
/// </summary>
public sealed record LastReplayOutcomeResponse(
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// One other namespace where this exact signature hash has also been observed — same
/// dominant deadletter reason and top terms as the signature being investigated, since the
/// hash is derived from those alone (see <c>ClusterSignatureHasher</c>). <see cref="Knowledge"/>
/// is <see langword="null"/> when no root cause has been recorded for it in that namespace.
/// <see cref="LastReplayOutcome"/> is <see langword="null"/> when the signature has never been
/// replayed in that namespace.
/// </summary>
public sealed record RootCauseMatchResponse(
    Guid NamespaceId,
    int OccurrenceCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string LifecycleStatus,
    KnowledgeResponse? Knowledge,
    LastReplayOutcomeResponse? LastReplayOutcome);

/// <summary>
/// Fleet-wide view of one failure signature: every other namespace it has also occurred in,
/// and the resulting total occurrence count across the whole fleet. Answers "has this happened
/// before anywhere in my fleet?" using only already-persisted <c>NamespaceSignature</c> rows —
/// no scoring, no inference.
/// </summary>
public sealed record RootCauseExplorerResponse(
    string SignatureHash,
    string DominantDeadletterReason,
    IReadOnlyList<string> TopTerms,
    int TotalOccurrencesAcrossFleet,
    IReadOnlyList<RootCauseMatchResponse> Matches);
