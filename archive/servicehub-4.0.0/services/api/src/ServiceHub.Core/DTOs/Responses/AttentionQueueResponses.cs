namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// One entry in the Home attention queue (roadmap W2.2) — a failure signature ranked by the
/// four axes the roadmap names: severity, blast radius, recurrence, and whether a human decision
/// is blocking. Reuses <see cref="IncidentDetailResponse"/>'s identity fields so a client can
/// deep-link straight from a card to <c>GET /api/v1/namespaces/{namespaceId}/incidents/{signatureHash}</c>.
/// </summary>
/// <param name="SignatureHash">The signature's stable identity hash.</param>
/// <param name="NamespaceId">The namespace this signature belongs to.</param>
/// <param name="NamespaceName">The namespace's display name, if resolvable.</param>
/// <param name="DisplayName">A human-readable label for this signature.</param>
/// <param name="LifecycleStatus">The signature's current lifecycle status.</param>
/// <param name="Severity">The owning namespace's <c>FleetHealthSeverity</c> (roadmap F5's only
/// existing severity concept), as a string: <c>Critical</c>/<c>Warning</c>/<c>Healthy</c>/<c>Unknown</c>.</param>
/// <param name="BlastRadius">Dead-lettered message volume for this signature (<c>NamespaceSignature.OccurrenceCount</c>).</param>
/// <param name="IsRecurring">Whether this signature reopened after previously being marked resolved.</param>
/// <param name="PendingDecisionCount">Recovery/Playbook entries currently blocked on a human — see
/// <see cref="IncidentSummary.PendingDecisionCount"/>. A nonzero count is why this item is in the
/// queue at all, regardless of its <see cref="Score"/> rank: pending approvals are never dropped
/// by the top-N cap.</param>
/// <param name="Score">The additive ranking score used to order and cap the queue. Exposed for
/// debuggability, not a stable public contract.</param>
/// <param name="RecommendedAction">A short, plain-language next step.</param>
/// <param name="LastSeenAt">When this signature was last observed.</param>
public sealed record AttentionQueueItem(
    string SignatureHash,
    Guid NamespaceId,
    string? NamespaceName,
    string DisplayName,
    string LifecycleStatus,
    string Severity,
    int BlastRadius,
    bool IsRecurring,
    int PendingDecisionCount,
    double Score,
    string RecommendedAction,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Response DTO for <c>GET /api/v1/attention-queue</c> (W2.2) — Home as a ranked attention queue.
/// At most the service's top-N cap of items, ordered worst-first. An empty
/// <see cref="Items"/> list is a genuine "nothing needs you right now" signal, not a loading or
/// error state — <see cref="IsEmpty"/> lets the client render "everything looks healthy" instead
/// of inferring it from an empty array.
/// </summary>
/// <param name="Items">The ranked, capped queue items, worst-first.</param>
/// <param name="IsEmpty">True when nothing needs attention right now.</param>
public sealed record AttentionQueueResponse(
    IReadOnlyList<AttentionQueueItem> Items,
    bool IsEmpty);
