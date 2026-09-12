using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Aggregates failure signatures, replay jobs, and knowledge gaps into an
/// incident command center view. Composes results from existing services
/// (no new queries, no new data access layer).
/// </summary>
public sealed class FailureIntelligenceCenterService : IFailureIntelligenceCenterService
{
    private const int TopUnhealthyNamespaceCount = 5;

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceSignatureLookupService _signatureLookup;
    private readonly ISignatureLifecycleService _lifecycle;
    private readonly IFailureKnowledgeService _knowledge;
    private readonly IFleetOverviewService _fleetOverview;
    private readonly INamespaceRepository _namespaceRepository;

    public FailureIntelligenceCenterService(
        DlqDbContext dbContext,
        INamespaceSignatureLookupService signatureLookup,
        ISignatureLifecycleService lifecycle,
        IFailureKnowledgeService knowledge,
        IFleetOverviewService fleetOverview,
        INamespaceRepository namespaceRepository)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _signatureLookup = signatureLookup ?? throw new ArgumentNullException(nameof(signatureLookup));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _fleetOverview = fleetOverview ?? throw new ArgumentNullException(nameof(fleetOverview));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
    }

    public async Task<Result<InvestigationCenterResponse>> GetInvestigationCenterAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        // Fingerprint-space only (M1.4, ADR-0009): Incident Center's "Investigate" deep link
        // opens IncidentReadModelService.GetIncidentAsync, which only ever resolves a Fingerprint-
        // kind row (the identity AutonomyGrants and the attention queue key on) — mirrors
        // AttentionQueueService's identical filter for the identical reason. Without this filter,
        // a Cluster-kind row for the same real failure (written by the DLQ Intelligence
        // clustering path, `GET /api/v1/.../dlq/signatures`) surfaces here as an "incident" whose
        // own "Investigate" link 404s.
        var signatures = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.HashKind == SignatureHashKind.Fingerprint)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Deleting a namespace does not cascade-delete its signature rows, so without this
        // filter a deleted namespace's stale signatures would keep surfacing here (and in the
        // "Investigate" deep link) indefinitely. Mirrors FleetOverviewService.GetOverviewAsync's
        // namespace-registry filtering.
        var registeredNamespacesResult = await _namespaceRepository.GetByOwnerAsync(
            ownerId, allowedNamespaceIds: null, cancellationToken).ConfigureAwait(false);
        if (registeredNamespacesResult.IsSuccess)
        {
            var registeredNamespaceIds = registeredNamespacesResult.Value.Select(n => n.Id).ToHashSet();
            signatures = signatures.Where(s => registeredNamespaceIds.Contains(s.NamespaceId)).ToList();
        }

        // Get all knowledge records for this owner
        var allKnowledge = await _dbContext.FailureKnowledgeEntities
            .AsNoTracking()
            .Where(k => k.OwnerId == ownerId)
            .ToDictionaryAsync(k => (k.NamespaceId, k.SignatureHash), cancellationToken)
            .ConfigureAwait(false);

        // Get lifecycle status for all signatures
        var allSignatureHashes = signatures.Select(s => s.SignatureHash).Distinct().ToList();
        var lifecycleByHash = new Dictionary<(Guid, string), SignatureLifecycleSnapshot>();

        foreach (var signature in signatures)
        {
            var lifecycleResult = await _lifecycle.GetStatusAsync(
                ownerId, signature.NamespaceId, signature.SignatureHash, cancellationToken)
                .ConfigureAwait(false);

            if (lifecycleResult.IsSuccess)
            {
                lifecycleByHash[(signature.NamespaceId, signature.SignatureHash)] = lifecycleResult.Value;
            }
        }

        // Compute metrics
        var metrics = ComputeMetrics(signatures, lifecycleByHash);

        // Build investigation queue (active, escalating, or without knowledge)
        var investigationQueue = BuildInvestigationQueue(signatures, lifecycleByHash, allKnowledge);

        // Build failed replays section (from job store, last 7 days)
        var failedReplays = await BuildFailedReplaysAsync(signatures, ownerId, cancellationToken);

        // Build knowledge review section (overdue or missing)
        var knowledgeReview = BuildKnowledgeReview(signatures, lifecycleByHash, allKnowledge);

        // Build new signatures section (no knowledge)
        var newSignatures = BuildNewSignatures(signatures, lifecycleByHash, allKnowledge);

        // Build recently changed section (recent lifecycle events, knowledge updates, replay completions)
        var recentlyChanged = await BuildRecentlyChangedAsync(ownerId, cancellationToken);

        // Build fleet health summary (composes IFleetOverviewService; null if the query fails)
        var fleetHealth = await BuildFleetHealthAsync(ownerId, cancellationToken);

        return Result.Success(new InvestigationCenterResponse(
            metrics,
            investigationQueue,
            failedReplays,
            knowledgeReview,
            newSignatures,
            recentlyChanged,
            fleetHealth));
    }

    private async Task<FleetHealthSummary?> BuildFleetHealthAsync(
        string ownerId,
        CancellationToken cancellationToken)
    {
        var overviewResult = await _fleetOverview.GetOverviewAsync(ownerId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!overviewResult.IsSuccess)
        {
            return null;
        }

        var overview = overviewResult.Value;
        var topUnhealthy = overview.Namespaces
            .Where(n => n.Severity is FleetHealthSeverity.Warning or FleetHealthSeverity.Critical)
            .Take(TopUnhealthyNamespaceCount)
            .ToList();

        return new FleetHealthSummary(
            overview.NamespaceCount,
            overview.TotalActive,
            overview.TotalNewInWindow,
            overview.TotalResolvedInWindow,
            topUnhealthy);
    }

    private static CompactMetricsSummary ComputeMetrics(
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash)
    {
        var total = signatures.Count;
        var active = 0;
        var resolved = 0;
        var suppressed = 0;
        var archived = 0;
        var requiresAction = 0;

        foreach (var sig in signatures)
        {
            var key = (sig.NamespaceId, sig.SignatureHash);
            var lifecycle = lifecycleByHash.TryGetValue(key, out var ls)
                ? ls
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            switch (lifecycle.Status)
            {
                case SignatureLifecycleStatus.Active:
                    active++;
                    requiresAction++;
                    break;
                case SignatureLifecycleStatus.Reopened:
                    requiresAction++;
                    break;
                case SignatureLifecycleStatus.Resolved:
                    resolved++;
                    break;
                case SignatureLifecycleStatus.Suppressed:
                    suppressed++;
                    break;
                case SignatureLifecycleStatus.Archived:
                    archived++;
                    break;
            }
        }

        return new CompactMetricsSummary(total, active, resolved, suppressed, archived, requiresAction);
    }

    private static List<InvestigationQueueItem> BuildInvestigationQueue(
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash,
        Dictionary<(Guid, string), FailureKnowledgeEntity> allKnowledge)
    {
        var items = new List<InvestigationQueueItem>();

        foreach (var sig in signatures)
        {
            var key = (sig.NamespaceId, sig.SignatureHash);
            var lifecycle = lifecycleByHash.TryGetValue(key, out var ls)
                ? ls
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            // Only include active and reopened signatures
            if (lifecycle.Status is not (SignatureLifecycleStatus.Active or SignatureLifecycleStatus.Reopened))
                continue;

            var hasKnowledge = allKnowledge.ContainsKey(key);
            var isEscalating = lifecycle.PreviousStatus == SignatureLifecycleStatus.Resolved;

            // Compute priority score: escalating (10), no knowledge (5), high count (3), recent (2)
            var score = 0.0;
            if (isEscalating) score += 10;
            if (!hasKnowledge) score += 5;
            if (sig.OccurrenceCount > 10) score += 3;

            var daysSinceSeen = (DateTimeOffset.UtcNow - sig.LastSeenAt).TotalDays;
            if (daysSinceSeen < 1) score += 2;

            var recommendedAction = DetermineRecommendedAction(hasKnowledge, isEscalating);

            items.Add(new InvestigationQueueItem(
                sig.SignatureHash,
                sig.NamespaceId,
                $"{sig.DominantDeadletterReason} (ID: {sig.SignatureHash[..8]})",
                sig.DominantDeadletterReason,
                sig.OccurrenceCount,
                lifecycle.Status.ToString(),
                isEscalating ? "Escalating" : "Active",
                score,
                hasKnowledge,
                isEscalating,
                allKnowledge.TryGetValue(key, out var k) ? k.Owner : null,
                recommendedAction,
                null));
        }

        // Order by priority score (descending)
        return items.OrderByDescending(i => i.PriorityScore).ToList();
    }

    /// <summary>
    /// Signatures whose most recent signature-replay job, within the last 7 days, did not
    /// complete cleanly — the durable <see cref="SignatureReplayJob"/> data this section was
    /// scaffolded for but never wired to. A signature whose latest job in the window succeeded
    /// (even if an earlier one failed) is not included: this reflects current replay health, not
    /// a lifetime failure count.
    /// </summary>
    private async Task<List<FailedReplayItem>> BuildFailedReplaysAsync(
        IReadOnlyList<NamespaceSignature> signatures,
        string ownerId,
        CancellationToken cancellationToken)
    {
        var windowStart = DateTimeOffset.UtcNow.AddDays(-7);

        var recentJobs = await _dbContext.SignatureReplayJobs
            .AsNoTracking()
            .Where(j => j.OwnerId == ownerId && j.CreatedAt >= windowStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latestUnsuccessfulPerSignature = recentJobs
            .GroupBy(j => (j.NamespaceId, j.SignatureHash))
            .Select(g => g.OrderByDescending(j => j.CreatedAt).First())
            .Where(j => j.Status is BulkOperationStatus.Failed or BulkOperationStatus.CompletedWithErrors)
            .ToList();

        var signatureByKey = signatures.ToDictionary(s => (s.NamespaceId, s.SignatureHash));

        var items = new List<FailedReplayItem>(latestUnsuccessfulPerSignature.Count);
        foreach (var job in latestUnsuccessfulPerSignature)
        {
            var displayName = signatureByKey.TryGetValue((job.NamespaceId, job.SignatureHash), out var sig)
                ? $"{sig.DominantDeadletterReason} (ID: {sig.SignatureHash[..8]})"
                : $"Signature {job.SignatureHash[..Math.Min(8, job.SignatureHash.Length)]}";

            var failureReason = job.ErrorSummary
                ?? (job.FailureCount > 0 ? $"{job.FailureCount} of {job.TotalMatched} message(s) failed." : null);

            items.Add(new FailedReplayItem(
                JobId: job.Id,
                NamespaceId: job.NamespaceId,
                SignatureHash: job.SignatureHash,
                SignatureName: displayName,
                JobStatus: job.Status.ToString(),
                FailureReason: failureReason,
                CreatedAt: job.CreatedAt,
                CompletedAt: job.CompletedAt,
                AttemptedCount: job.ProcessedCount,
                FailedCount: job.FailureCount,
                RecommendedNextAction: DetermineReplayRecommendedAction(job.Status)));
        }

        return items.OrderByDescending(i => i.CreatedAt).ToList();
    }

    private static string DetermineReplayRecommendedAction(BulkOperationStatus status) => status switch
    {
        BulkOperationStatus.Failed => "Investigate the underlying failure before replaying again.",
        BulkOperationStatus.CompletedWithErrors => "Review the failure sample before retrying.",
        _ => "Review before replaying again.",
    };

    private static List<KnowledgeReviewItem> BuildKnowledgeReview(
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash,
        Dictionary<(Guid, string), FailureKnowledgeEntity> allKnowledge)
    {
        var items = new List<KnowledgeReviewItem>();
        var now = DateTimeOffset.UtcNow;

        foreach (var sig in signatures)
        {
            var key = (sig.NamespaceId, sig.SignatureHash);
            var lifecycle = lifecycleByHash.TryGetValue(key, out var ls)
                ? ls
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            // Skip archived and resolved
            if (lifecycle.Status is SignatureLifecycleStatus.Archived or SignatureLifecycleStatus.Resolved)
                continue;

            var hasKnowledge = allKnowledge.TryGetValue(key, out var knowledge);
            var isReviewOverdue = hasKnowledge && knowledge is not null && knowledge.ReviewDueAt.HasValue && knowledge.ReviewDueAt.Value < now;

            // Include if missing knowledge or review is overdue
            if (!hasKnowledge || isReviewOverdue)
            {
                var recommendedAction = !hasKnowledge || knowledge is null ? "Add Knowledge" : "Update Knowledge";

                items.Add(new KnowledgeReviewItem(
                    sig.SignatureHash,
                    sig.NamespaceId,
                    $"{sig.DominantDeadletterReason} (ID: {sig.SignatureHash[..8]})",
                    sig.OccurrenceCount,
                    lifecycle.Status.ToString(),
                    knowledge?.Owner,
                    hasKnowledge && knowledge is not null,
                    isReviewOverdue,
                    knowledge?.ReviewDueAt,
                    knowledge?.LastUpdatedAt,
                    recommendedAction));
            }
        }

        // Order by urgency: overdue first, then missing, then by review date
        return items
            .OrderByDescending(i => i.IsReviewOverdue)
            .ThenBy(i => i.HasKnowledge)
            .ThenBy(i => i.ReviewDueAt)
            .ToList();
    }

    private static List<NewSignatureItem> BuildNewSignatures(
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash,
        Dictionary<(Guid, string), FailureKnowledgeEntity> allKnowledge)
    {
        var items = new List<NewSignatureItem>();

        foreach (var sig in signatures)
        {
            var key = (sig.NamespaceId, sig.SignatureHash);
            var hasKnowledge = allKnowledge.ContainsKey(key);

            // Only include if no knowledge and active/reopened
            if (hasKnowledge)
                continue;

            var lifecycle = lifecycleByHash.TryGetValue(key, out var ls)
                ? ls
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            if (lifecycle.Status is SignatureLifecycleStatus.Archived or SignatureLifecycleStatus.Resolved)
                continue;

            items.Add(new NewSignatureItem(
                sig.SignatureHash,
                sig.NamespaceId,
                $"{sig.DominantDeadletterReason} (ID: {sig.SignatureHash[..8]})",
                sig.DominantDeadletterReason,
                sig.OccurrenceCount,
                sig.FirstSeenAt,
                sig.LastSeenAt,
                null,
                "Add Knowledge"));
        }

        // Order by recency (most recent first)
        return items.OrderByDescending(i => i.LastSeenAt).ToList();
    }

    private static Task<List<RecentChangeItem>> BuildRecentlyChangedAsync(
        string ownerId,
        CancellationToken cancellationToken)
    {
        var items = new List<RecentChangeItem>();

        // Lifecycle transition history is now durable and queryable
        // (ISignatureLifecycleService.GetHistoryAsync, backed by SignatureLifecycleHistory) —
        // wiring this section to that data is a future enhancement, out of scope for the
        // durability work that made it possible. Knowledge updates can be sourced from
        // FailureKnowledgeHistoryEntity once we add queries for them.

        return Task.FromResult(items);
    }

    private static string DetermineRecommendedAction(bool hasKnowledge, bool isEscalating)
    {
        if (isEscalating) return "Review Escalation";
        if (!hasKnowledge) return "Add Knowledge";
        return "Investigate";
    }

    /// <inheritdoc />
    public async Task<Result<IncidentListResponse>> GetIncidentsListAsync(
        string ownerId,
        int trendDays,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var registeredNamespacesResult = await _namespaceRepository.GetByOwnerAsync(
            ownerId, allowedNamespaceIds: null, cancellationToken).ConfigureAwait(false);
        var namespacesById = registeredNamespacesResult.IsSuccess
            ? registeredNamespacesResult.Value.ToDictionary(n => n.Id)
            : new Dictionary<Guid, Namespace>();

        // Fingerprint-space only (M1.4, ADR-0009) — see GetInvestigationCenterAsync's identical
        // filter for why: this list feeds the same "Investigate" deep link.
        var signatures = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.HashKind == SignatureHashKind.Fingerprint)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Same orphaned-namespace filtering as GetInvestigationCenterAsync — a deleted
        // namespace's signature rows are not cascade-deleted.
        if (registeredNamespacesResult.IsSuccess)
        {
            signatures = signatures.Where(s => namespacesById.ContainsKey(s.NamespaceId)).ToList();
        }

        var lifecycleByHash = await _dbContext.SignatureLifecycleStates
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .ToDictionaryAsync(
                s => (s.NamespaceId, s.SignatureHash),
                s => new SignatureLifecycleSnapshot(s.Status, s.PreviousStatus, s.TransitionedAt, s.Notes),
                cancellationToken)
            .ConfigureAwait(false);

        var allKnowledge = await _dbContext.FailureKnowledgeEntities
            .AsNoTracking()
            .Where(k => k.OwnerId == ownerId)
            .ToDictionaryAsync(k => (k.NamespaceId, k.SignatureHash), cancellationToken)
            .ConfigureAwait(false);

        var metrics = ComputeMetrics(signatures, lifecycleByHash);
        var items = BuildIncidentListItems(signatures, lifecycleByHash, allKnowledge, namespacesById);
        var topCategories = BuildTopCategories(signatures);
        var trend = await BuildTrendAsync(ownerId, signatures, lifecycleByHash, namespacesById.Keys.ToHashSet(), trendDays, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new IncidentListResponse(metrics, trend, topCategories, items, DateTimeOffset.UtcNow));
    }

    private static List<IncidentListItem> BuildIncidentListItems(
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash,
        Dictionary<(Guid, string), FailureKnowledgeEntity> allKnowledge,
        Dictionary<Guid, Namespace> namespacesById)
    {
        var items = new List<IncidentListItem>(signatures.Count);

        foreach (var sig in signatures)
        {
            var key = (sig.NamespaceId, sig.SignatureHash);
            var lifecycle = lifecycleByHash.TryGetValue(key, out var ls)
                ? ls
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            var hasKnowledge = allKnowledge.TryGetValue(key, out var knowledge);
            var isEscalating = lifecycle.PreviousStatus == SignatureLifecycleStatus.Resolved;
            var isActionable = lifecycle.Status is SignatureLifecycleStatus.Active or SignatureLifecycleStatus.Reopened;
            var recommendedAction = isActionable ? DetermineRecommendedAction(hasKnowledge, isEscalating) : null;
            var severity = ComputeSeverityLevel(ComputeSeverityScore(isEscalating, hasKnowledge, sig.OccurrenceCount, sig.LastSeenAt));

            namespacesById.TryGetValue(sig.NamespaceId, out var ns);

            items.Add(new IncidentListItem(
                sig.SignatureHash,
                sig.NamespaceId,
                ns?.DisplayName ?? ns?.Name,
                ns?.Provider,
                ns?.Environment,
                $"{sig.DominantDeadletterReason} (ID: {sig.SignatureHash[..8]})",
                sig.DominantDeadletterReason,
                severity,
                lifecycle.Status.ToString(),
                isEscalating,
                sig.OccurrenceCount,
                sig.FirstSeenAt,
                sig.LastSeenAt,
                hasKnowledge,
                hasKnowledge ? knowledge!.Owner : null,
                recommendedAction));
        }

        return items.OrderByDescending(i => i.LastSeenAt).ToList();
    }

    /// <summary>
    /// Same priority heuristic <see cref="BuildInvestigationQueue"/> uses (escalating +10, no
    /// knowledge +5, high occurrence count +3, seen in the last day +2), applied to every
    /// signature regardless of lifecycle status — severity is a quality-of-the-failure signal,
    /// not an actionability one.
    /// </summary>
    private static double ComputeSeverityScore(bool isEscalating, bool hasKnowledge, int occurrenceCount, DateTimeOffset lastSeenAt)
    {
        var score = 0.0;
        if (isEscalating) score += 10;
        if (!hasKnowledge) score += 5;
        if (occurrenceCount > 10) score += 3;
        if ((DateTimeOffset.UtcNow - lastSeenAt).TotalDays < 1) score += 2;
        return score;
    }

    /// <summary>Mirrors the frontend's <c>getPriorityLevel</c> thresholds (FailureIntelligenceCenterPage) so the
    /// server-computed severity always agrees with how the UI would have classified the same score.</summary>
    private static string ComputeSeverityLevel(double score) => score switch
    {
        >= 15 => "Critical",
        >= 10 => "High",
        >= 5 => "Medium",
        _ => "Low",
    };

    /// <summary>
    /// Total message occurrences (not signature count) per dominant deadletter reason, top 5 plus
    /// an "Others" bucket for the rest — mirrors <c>DlqOverviewPage</c>'s client-side top-reasons
    /// rollup, computed here instead since it folds over the full fleet-wide signature set.
    /// </summary>
    private static List<IncidentCategoryBreakdown> BuildTopCategories(IReadOnlyList<NamespaceSignature> signatures)
    {
        var total = signatures.Sum(s => s.OccurrenceCount);
        if (total == 0) return [];

        var grouped = signatures
            .GroupBy(s => s.DominantDeadletterReason)
            .Select(g => (Category: g.Key, Count: g.Sum(s => s.OccurrenceCount)))
            .OrderByDescending(g => g.Count)
            .ToList();

        const int topN = 5;
        var top = grouped.Take(topN)
            .Select(g => new IncidentCategoryBreakdown(g.Category, g.Count, Math.Round(g.Count * 100.0 / total, 1)))
            .ToList();

        var othersCount = grouped.Skip(topN).Sum(g => g.Count);
        if (othersCount > 0)
        {
            top.Add(new IncidentCategoryBreakdown("Others", othersCount, Math.Round(othersCount * 100.0 / total, 1)));
        }

        return top;
    }

    /// <summary>
    /// See <see cref="IncidentTrendPoint"/> for exactly what each series means and why — this
    /// buckets real timestamps (signature first-seen, lifecycle-history transitions, signature
    /// last-seen) rather than reconstructing a historical point-in-time status index.
    /// </summary>
    private async Task<List<IncidentTrendPoint>> BuildTrendAsync(
        string ownerId,
        IReadOnlyList<NamespaceSignature> signatures,
        Dictionary<(Guid, string), SignatureLifecycleSnapshot> lifecycleByHash,
        HashSet<Guid> registeredNamespaceIds,
        int trendDays,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var hourly = trendDays <= 1;
        var bucketCount = hourly ? 24 : Math.Clamp(trendDays, 1, 30);
        var bucketSpan = hourly ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var windowStart = now - TimeSpan.FromTicks(bucketSpan.Ticks * bucketCount);

        var resolvedTransitions = await _dbContext.SignatureLifecycleHistory
            .AsNoTracking()
            .Where(h => h.OwnerId == ownerId
                && h.ToStatus == SignatureLifecycleStatus.Resolved
                && h.Timestamp >= windowStart)
            .Select(h => new { h.NamespaceId, h.Timestamp })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var resolvedInWindow = resolvedTransitions.Where(h => registeredNamespaceIds.Contains(h.NamespaceId)).ToList();

        var points = new List<IncidentTrendPoint>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = windowStart + TimeSpan.FromTicks(bucketSpan.Ticks * i);
            var bucketEnd = bucketStart + bucketSpan;

            var newCount = signatures.Count(s => s.FirstSeenAt >= bucketStart && s.FirstSeenAt < bucketEnd);
            var resolvedCount = resolvedInWindow.Count(h => h.Timestamp >= bucketStart && h.Timestamp < bucketEnd);
            var activeCount = signatures.Count(s =>
            {
                if (s.LastSeenAt < bucketStart || s.LastSeenAt >= bucketEnd) return false;
                var status = lifecycleByHash.TryGetValue((s.NamespaceId, s.SignatureHash), out var ls)
                    ? ls.Status
                    : SignatureLifecycleStatus.Active;
                return status is SignatureLifecycleStatus.Active or SignatureLifecycleStatus.Reopened;
            });

            points.Add(new IncidentTrendPoint(bucketStart, activeCount, resolvedCount, newCount));
        }

        return points;
    }
}
