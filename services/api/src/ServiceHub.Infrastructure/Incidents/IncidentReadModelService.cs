using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Incidents;

/// <summary>
/// <inheritdoc cref="IIncidentReadModelService"/>
/// </summary>
/// <remarks>
/// Composes four existing reads — <see cref="DlqDbContext.NamespaceSignatures"/>,
/// <see cref="ISignatureLifecycleService.GetStatusAsync"/>, <see cref="IRecoveryLedger.FindEntriesForSignatureSinceAsync"/>,
/// and <see cref="IPlaybookLedger.QueryEntriesAsync"/> — the same "no new queries, no new data
/// access layer" discipline <c>FailureIntelligenceCenterService</c> already applies. Recovery
/// entries are joined signature-precisely, unscoped by namespace (mirroring <c>BacktestService</c>'s
/// W1.5 join — a <c>SignatureHashSnapshot</c> is already namespace/provider-specific by
/// construction); Playbook entries are joined by namespace via <c>QueryEntriesAsync</c> and then
/// narrowed to this signature client-side, since the ledger has no signature-scoped query.
/// </remarks>
public sealed class IncidentReadModelService : IIncidentReadModelService
{
    private const int RecoveryEntryLimit = 200;

    private static readonly IReadOnlySet<RecoveryEntryState> OpenRecoveryStates =
        new HashSet<RecoveryEntryState> { RecoveryEntryState.Executing, RecoveryEntryState.Observing };

    private static readonly IReadOnlySet<PlaybookEntryState> PendingPlaybookStates =
        new HashSet<PlaybookEntryState>
        {
            PlaybookEntryState.Proposed, PlaybookEntryState.UnderReview, PlaybookEntryState.Edited,
        };

    private readonly DlqDbContext _dbContext;
    private readonly ISignatureLifecycleService _lifecycle;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly INamespaceRepository _namespaceRepository;

    /// <summary>Initializes a new instance of <see cref="IncidentReadModelService"/>.</summary>
    public IncidentReadModelService(
        DlqDbContext dbContext,
        ISignatureLifecycleService lifecycle,
        IRecoveryLedger recoveryLedger,
        IPlaybookLedger playbookLedger,
        INamespaceRepository namespaceRepository)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
    }

    /// <inheritdoc/>
    public async Task<Result<IncidentDetailResponse>> GetIncidentAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        if (string.IsNullOrWhiteSpace(signatureHash))
        {
            return Result.Failure<IncidentDetailResponse>(Error.NotFound(
                "Incidents.SignatureNotFound", $"Incident '{signatureHash}' was not found."));
        }

        var signature = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.NamespaceId == namespaceId && s.SignatureHash == signatureHash,
                cancellationToken)
            .ConfigureAwait(false);

        var recoveryEntries = await _recoveryLedger.FindEntriesForSignatureSinceAsync(
            ownerId, signatureHash, DateTimeOffset.UnixEpoch, RecoveryEntryLimit, cancellationToken)
            .ConfigureAwait(false);

        var playbookResult = await _playbookLedger.QueryEntriesAsync(
            ownerId, pillarKind: null, namespaceId: namespaceId, state: null, cancellationToken)
            .ConfigureAwait(false);
        var playbookEntries = playbookResult.IsSuccess
            ? playbookResult.Value.Where(e => e.SignatureHashSnapshot == signatureHash).ToList()
            : new List<PlaybookEntry>();

        if (signature is null && recoveryEntries.Count == 0 && playbookEntries.Count == 0)
        {
            return Result.Failure<IncidentDetailResponse>(Error.NotFound(
                "Incidents.SignatureNotFound", $"Incident '{signatureHash}' was not found."));
        }

        var lifecycleResult = await _lifecycle.GetStatusAsync(ownerId, namespaceId, signatureHash, cancellationToken)
            .ConfigureAwait(false);
        var lifecycleStatus = lifecycleResult.IsSuccess
            ? lifecycleResult.Value.Status.ToString()
            : SignatureLifecycleStatus.Active.ToString();

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken)
            .ConfigureAwait(false);
        var namespaceName = namespaceResult.IsSuccess
            ? namespaceResult.Value.DisplayName ?? namespaceResult.Value.Name
            : recoveryEntries.FirstOrDefault()?.NamespaceNameSnapshot
                ?? playbookEntries.FirstOrDefault()?.NamespaceNameSnapshot;

        var summary = BuildSummary(recoveryEntries, playbookEntries);

        return Result.Success(new IncidentDetailResponse(
            SignatureHash: signatureHash,
            NamespaceId: namespaceId,
            NamespaceName: namespaceName,
            LifecycleStatus: lifecycleStatus,
            FirstSeenAt: signature?.FirstSeenAt
                ?? recoveryEntries.Select(e => e.BegunAt).Concat(playbookEntries.Select(e => e.ProposedAt)).DefaultIfEmpty(DateTimeOffset.UtcNow).Min(),
            LastSeenAt: signature?.LastSeenAt
                ?? recoveryEntries.Select(e => e.BegunAt).Concat(playbookEntries.Select(e => e.ProposedAt)).DefaultIfEmpty(DateTimeOffset.UtcNow).Max(),
            OccurrenceCount: signature?.OccurrenceCount ?? 0,
            DominantDeadletterReason: signature?.DominantDeadletterReason,
            TopTerms: ExtractTopTerms(signature),
            Summary: summary,
            RecoveryEntries: recoveryEntries.Select(MapRecoveryEntry).ToList(),
            PlaybookEntries: playbookEntries.Select(MapPlaybookEntry).ToList()));
    }

    private static IncidentSummary BuildSummary(
        IReadOnlyList<RecoveryLedgerEntry> recoveryEntries, IReadOnlyList<PlaybookEntry> playbookEntries) =>
        new(
            RecoveryEntryCount: recoveryEntries.Count,
            OpenRecoveryEntryCount: recoveryEntries.Count(e => OpenRecoveryStates.Contains(e.State)),
            PendingDecisionCount: recoveryEntries.Count(e => e.State == RecoveryEntryState.Declined)
                + playbookEntries.Count(e => PendingPlaybookStates.Contains(e.State)),
            AnomalyFlagCount: playbookEntries.Count(e => e.ProposalKind == "AnomalyFlag"),
            DriftFindingCount: playbookEntries.Count(e => e.ProposalKind == "DriftFinding"),
            CorrelationHypothesisCount: playbookEntries.Count(e => e.ProposalKind == "CorrelationHypothesis"),
            PreventionTriggerCount: playbookEntries.Count(e => e.ProposalKind == "PreventionTrigger"),
            ReplayPlanCount: playbookEntries.Count(e => e.ProposalKind == "ReplayPlan"));

    private static IReadOnlyList<string> ExtractTopTerms(NamespaceSignature? signature)
    {
        if (signature is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(signature.TopTermsJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static RecoveryLedgerEntryResponse MapRecoveryEntry(RecoveryLedgerEntry entry) => new(
        Id: entry.Id,
        OperationId: entry.OperationId,
        DlqMessageId: entry.DlqMessageId,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        EntityNameSnapshot: entry.EntityNameSnapshot,
        EntityTypeSnapshot: entry.EntityTypeSnapshot,
        TopicNameSnapshot: entry.TopicNameSnapshot,
        BodyHash: entry.BodyHash,
        FailureCategorySnapshot: entry.FailureCategorySnapshot?.ToString(),
        DeadLetterReasonSnapshot: entry.DeadLetterReasonSnapshot,
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        TargetEntity: entry.TargetEntity,
        BegunAt: entry.BegunAt,
        MarkerApplied: entry.MarkerApplied,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        VerificationResult: entry.VerificationResult?.ToString(),
        VerificationConfidence: entry.VerificationConfidence?.ToString(),
        ObservationWindowEndsAt: entry.ObservationWindowEndsAt,
        ClosedAt: entry.ClosedAt);

    private static PlaybookEntryResponse MapPlaybookEntry(PlaybookEntry entry) => new(
        Id: entry.Id,
        PillarKind: entry.PillarKind.ToString(),
        ProposalKind: entry.ProposalKind,
        EvidenceRefJson: entry.EvidenceRefJson,
        ProposalJson: entry.ProposalJson,
        ProposedAt: entry.ProposedAt,
        ProposerIdentity: entry.ProposerIdentity,
        ProposerKind: entry.ProposerKind.ToString(),
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        RelatedRecoveryOperationId: entry.RelatedRecoveryOperationId,
        ExpiresAt: entry.ExpiresAt,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        ClosedAt: entry.ClosedAt);
}
