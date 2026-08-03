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
    private readonly DlqDbContext _dbContext;
    private readonly INamespaceSignatureLookupService _signatureLookup;
    private readonly ISignatureLifecycleService _lifecycle;
    private readonly IFailureKnowledgeService _knowledge;
    private readonly ISignatureReplayJobStore _replayJobStore;

    public FailureIntelligenceCenterService(
        DlqDbContext dbContext,
        INamespaceSignatureLookupService signatureLookup,
        ISignatureLifecycleService lifecycle,
        IFailureKnowledgeService knowledge,
        ISignatureReplayJobStore replayJobStore)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _signatureLookup = signatureLookup ?? throw new ArgumentNullException(nameof(signatureLookup));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _replayJobStore = replayJobStore ?? throw new ArgumentNullException(nameof(replayJobStore));
    }

    public async Task<Result<InvestigationCenterResponse>> GetInvestigationCenterAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        // Get all signatures for this owner across all namespaces
        var signatures = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

        return Result.Success(new InvestigationCenterResponse(
            metrics,
            investigationQueue,
            failedReplays,
            knowledgeReview,
            newSignatures,
            recentlyChanged));
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

    private static Task<List<FailedReplayItem>> BuildFailedReplaysAsync(
        IReadOnlyList<NamespaceSignature> signatures,
        string ownerId,
        CancellationToken cancellationToken)
    {
        var items = new List<FailedReplayItem>();

        // For MVP: replay jobs are in-memory only in ISignatureReplayJobStore.
        // Populating this section requires making the job store queryable or
        // adding a durable replay job table. Both are future enhancements.

        return Task.FromResult(items);
    }

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

        // For MVP: Lifecycle events are in-memory only in ISignatureLifecycleService.
        // Populating this section requires adding a durable lifecycle history table,
        // which is a future enhancement. Knowledge updates can be sourced from
        // FailureKnowledgeHistoryEntity once we add queries for them.

        return Task.FromResult(items);
    }

    private static string DetermineRecommendedAction(bool hasKnowledge, bool isEscalating)
    {
        if (isEscalating) return "Review Escalation";
        if (!hasKnowledge) return "Add Knowledge";
        return "Investigate";
    }
}
