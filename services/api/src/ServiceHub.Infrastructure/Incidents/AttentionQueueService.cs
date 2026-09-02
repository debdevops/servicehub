using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Incidents;

/// <summary>
/// <inheritdoc cref="IAttentionQueueService"/>
/// </summary>
/// <remarks>
/// Composes the same "no new queries, no new data access layer" reads
/// <see cref="FailureIntelligenceCenterService"/> and <see cref="IncidentReadModelService"/>
/// already use: <see cref="DlqDbContext.NamespaceSignatures"/> and
/// <see cref="ISignatureLifecycleService.GetStatusAsync"/> for candidate identity/status,
/// <see cref="IRecoveryLedger.QueryEntriesAsync(RecoveryEntryQuery, CancellationToken)"/> (one
/// owner-wide call, filtered to <see cref="RecoveryEntryState.Declined"/>) and
/// <see cref="IPlaybookLedger.QueryEntriesAsync"/> (one owner-wide call, filtered client-side to
/// the pending states) for <see cref="IncidentSummary.PendingDecisionCount"/>'s two inputs, and
/// <see cref="IFleetOverviewService.GetOverviewAsync"/> for the one genuine per-namespace
/// severity concept the codebase has (<see cref="FleetHealthSeverity"/>).
/// </remarks>
public sealed class AttentionQueueService : IAttentionQueueService
{
    /// <summary>"Three cards maximum" — roadmap W2.2.</summary>
    private const int TopCount = 3;

    private const double DecisionBlockingWeight = 100;
    private const double RecurrenceWeight = 40;
    private const double SeverityCriticalWeight = 30;
    private const double SeverityWarningWeight = 15;
    private const double SeverityUnknownWeight = 5;
    private const int BlastRadiusCap = 100;

    private static readonly IReadOnlySet<PlaybookEntryState> PendingPlaybookStates =
        new HashSet<PlaybookEntryState>
        {
            PlaybookEntryState.Proposed, PlaybookEntryState.UnderReview, PlaybookEntryState.Edited,
        };

    private readonly DlqDbContext _dbContext;
    private readonly ISignatureLifecycleService _lifecycle;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly IFleetOverviewService _fleetOverview;
    private readonly INamespaceRepository _namespaceRepository;

    /// <summary>Initializes a new instance of <see cref="AttentionQueueService"/>.</summary>
    public AttentionQueueService(
        DlqDbContext dbContext,
        ISignatureLifecycleService lifecycle,
        IRecoveryLedger recoveryLedger,
        IPlaybookLedger playbookLedger,
        IFleetOverviewService fleetOverview,
        INamespaceRepository namespaceRepository)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _fleetOverview = fleetOverview ?? throw new ArgumentNullException(nameof(fleetOverview));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
    }

    /// <inheritdoc/>
    public async Task<Result<AttentionQueueResponse>> GetAttentionQueueAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var signatures = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Deleting a namespace does not cascade-delete its signature rows — mirrors
        // FailureIntelligenceCenterService's own filtering for the same reason.
        var registeredNamespacesResult = await _namespaceRepository.GetByOwnerAsync(
            ownerId, allowedNamespaceIds: null, cancellationToken).ConfigureAwait(false);
        var namespacesById = registeredNamespacesResult.IsSuccess
            ? registeredNamespacesResult.Value.ToDictionary(n => n.Id)
            : new Dictionary<Guid, Namespace>();
        if (registeredNamespacesResult.IsSuccess)
        {
            signatures = signatures.Where(s => namespacesById.ContainsKey(s.NamespaceId)).ToList();
        }

        if (signatures.Count == 0)
        {
            return Result.Success(new AttentionQueueResponse(Array.Empty<AttentionQueueItem>(), IsEmpty: true));
        }

        var declinedEntries = await _recoveryLedger.QueryEntriesAsync(
            new RecoveryEntryQuery { OwnerId = ownerId, States = new[] { RecoveryEntryState.Declined }, Limit = int.MaxValue },
            cancellationToken).ConfigureAwait(false);
        var declinedCountByHash = declinedEntries
            .Where(e => e.SignatureHashSnapshot is not null)
            .GroupBy(e => e.SignatureHashSnapshot!)
            .ToDictionary(g => g.Key, g => g.Count());

        var playbookResult = await _playbookLedger.QueryEntriesAsync(
            ownerId, pillarKind: null, namespaceId: null, state: null, cancellationToken).ConfigureAwait(false);
        var pendingPlaybookCountByHash = playbookResult.IsSuccess
            ? playbookResult.Value
                .Where(e => PendingPlaybookStates.Contains(e.State) && e.SignatureHashSnapshot is not null)
                .GroupBy(e => e.SignatureHashSnapshot!)
                .ToDictionary(g => g.Key, g => g.Count())
            : new Dictionary<string, int>();

        var overviewResult = await _fleetOverview.GetOverviewAsync(ownerId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var severityByNamespace = overviewResult.IsSuccess
            ? overviewResult.Value.Namespaces.ToDictionary(n => n.NamespaceId, n => n.Severity)
            : new Dictionary<Guid, FleetHealthSeverity>();

        var candidates = new List<(NamespaceSignature Signature, SignatureLifecycleStatus Status, bool IsEscalating, int PendingDecisionCount, FleetHealthSeverity Severity, double Score)>();

        foreach (var sig in signatures)
        {
            var lifecycleResult = await _lifecycle.GetStatusAsync(
                ownerId, sig.NamespaceId, sig.SignatureHash, cancellationToken).ConfigureAwait(false);
            var lifecycle = lifecycleResult.IsSuccess
                ? lifecycleResult.Value
                : new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null);

            var pendingDecisionCount = declinedCountByHash.GetValueOrDefault(sig.SignatureHash)
                + pendingPlaybookCountByHash.GetValueOrDefault(sig.SignatureHash);

            // "Pending approvals never hide behind a menu" — a signature with a human decision
            // blocking on it stays a candidate regardless of its lifecycle status; everything
            // else must still be Active/Reopened, mirroring the investigation queue's filter.
            var isActionable = lifecycle.Status is SignatureLifecycleStatus.Active or SignatureLifecycleStatus.Reopened;
            if (!isActionable && pendingDecisionCount == 0)
            {
                continue;
            }

            var isEscalating = lifecycle.PreviousStatus == SignatureLifecycleStatus.Resolved;
            var severity = severityByNamespace.GetValueOrDefault(sig.NamespaceId, FleetHealthSeverity.Unknown);

            var score = 0.0;
            if (pendingDecisionCount > 0) score += DecisionBlockingWeight;
            if (isEscalating) score += RecurrenceWeight;
            score += severity switch
            {
                FleetHealthSeverity.Critical => SeverityCriticalWeight,
                FleetHealthSeverity.Warning => SeverityWarningWeight,
                FleetHealthSeverity.Unknown => SeverityUnknownWeight,
                _ => 0,
            };
            score += Math.Min(sig.OccurrenceCount, BlastRadiusCap);

            candidates.Add((sig, lifecycle.Status, isEscalating, pendingDecisionCount, severity, score));
        }

        var top = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Signature.LastSeenAt)
            .Take(TopCount)
            .ToList();

        var items = top.Select(c => new AttentionQueueItem(
            SignatureHash: c.Signature.SignatureHash,
            NamespaceId: c.Signature.NamespaceId,
            NamespaceName: namespacesById.TryGetValue(c.Signature.NamespaceId, out var ns) ? ns.DisplayName ?? ns.Name : null,
            DisplayName: $"{c.Signature.DominantDeadletterReason} (ID: {c.Signature.SignatureHash[..8]})",
            LifecycleStatus: c.Status.ToString(),
            Severity: c.Severity.ToString(),
            BlastRadius: c.Signature.OccurrenceCount,
            IsRecurring: c.IsEscalating,
            PendingDecisionCount: c.PendingDecisionCount,
            Score: c.Score,
            RecommendedAction: DetermineRecommendedAction(c.PendingDecisionCount, c.IsEscalating),
            LastSeenAt: c.Signature.LastSeenAt))
            .ToList();

        return Result.Success(new AttentionQueueResponse(items, IsEmpty: items.Count == 0));
    }

    private static string DetermineRecommendedAction(int pendingDecisionCount, bool isEscalating)
    {
        if (pendingDecisionCount > 0) return "Review pending decision";
        if (isEscalating) return "Review escalation";
        return "Investigate";
    }
}
