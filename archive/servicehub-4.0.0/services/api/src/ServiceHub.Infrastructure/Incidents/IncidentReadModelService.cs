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

    /// <summary>Proposal kinds that never carry a <see cref="PlaybookEntry.SignatureHashSnapshot"/>
    /// by design (namespace+entity scoped, not signature scoped) but should still surface on the
    /// Incident Workspace's Evidence tab for the signature sharing their entity.</summary>
    private static readonly IReadOnlySet<string> EntityScopedEvidenceProposalKinds =
        new HashSet<string> { "AnomalyFlag", "DriftFinding", "CorrelationHypothesis" };

    private readonly DlqDbContext _dbContext;
    private readonly ISignatureLifecycleService _lifecycle;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly INamespaceSignatureLookupService _signatureLookupService;

    /// <summary>Initializes a new instance of <see cref="IncidentReadModelService"/>.</summary>
    public IncidentReadModelService(
        DlqDbContext dbContext,
        ISignatureLifecycleService lifecycle,
        IRecoveryLedger recoveryLedger,
        IPlaybookLedger playbookLedger,
        INamespaceRepository namespaceRepository,
        INamespaceSignatureLookupService signatureLookupService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _signatureLookupService = signatureLookupService ?? throw new ArgumentNullException(nameof(signatureLookupService));
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

        // Fingerprint-space only (M1.4, ADR-0009): an incident's signatureHash is always a trust
        // fingerprint — the same identity AutonomyGrants and the attention queue key on. A bare
        // hash-string match without this filter would (in the cryptographically near-impossible
        // case of a cross-space collision) resolve to the wrong row.
        var signature = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.NamespaceId == namespaceId
                    && s.SignatureHash == signatureHash && s.HashKind == SignatureHashKind.Fingerprint,
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
            // The requested hash may be a Cluster-space identity — e.g. a Failure Signature Detail
            // page's own URL, or its replay-completion "check verification status" link — rather
            // than the Fingerprint-space identity RecoveryLedgerEntries/PlaybookEntries/AutonomyGrants
            // key on (SignatureReplayExecutor.ResolveTrustSignatureHashAsync always writes recovery
            // ledger entries in Fingerprint space, on purpose, regardless of which space the caller
            // launched the replay from). Resolve to the sibling Fingerprint signature sharing entity +
            // dominant reason, same refuse-to-guess-if-ambiguous rule as
            // DlqHistoryController.ResolveEquivalentLiveCluster.
            var resolvedHash = await ResolveEquivalentFingerprintHashAsync(
                ownerId, namespaceId, signatureHash, cancellationToken).ConfigureAwait(false);

            if (resolvedHash is not null)
            {
                signatureHash = resolvedHash;
                signature = await _dbContext.NamespaceSignatures
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        s => s.OwnerId == ownerId && s.NamespaceId == namespaceId
                            && s.SignatureHash == signatureHash && s.HashKind == SignatureHashKind.Fingerprint,
                        cancellationToken)
                    .ConfigureAwait(false);

                recoveryEntries = await _recoveryLedger.FindEntriesForSignatureSinceAsync(
                    ownerId, signatureHash, DateTimeOffset.UnixEpoch, RecoveryEntryLimit, cancellationToken)
                    .ConfigureAwait(false);

                playbookEntries = playbookResult.IsSuccess
                    ? playbookResult.Value.Where(e => e.SignatureHashSnapshot == signatureHash).ToList()
                    : new List<PlaybookEntry>();
            }
        }

        if (signature is null && recoveryEntries.Count == 0 && playbookEntries.Count == 0)
        {
            return Result.Failure<IncidentDetailResponse>(Error.NotFound(
                "Incidents.SignatureNotFound", $"Incident '{signatureHash}' was not found."));
        }

        // AnomalyFlag/DriftFinding/CorrelationHypothesis proposals are deliberately namespace+entity
        // scoped, not signature scoped (PlaybookEntry.SignatureHashSnapshot is always null for them —
        // see AnomalyDetectionWorker/DriftDetectionWorker/CorrelationDetectionWorker's
        // ProposePlaybookEntryAsync, none of which populate it), so the exact-hash filter above never
        // matches them and the Evidence tab silently showed nothing for every signature. Link them in
        // here by the one attribute both vocabularies share: the entity name.
        if (playbookResult.IsSuccess)
        {
            var entityName = signature is not null
                ? ExtractTermValue(signature.TopTermsJson, "entity:")
                : recoveryEntries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.EntityNameSnapshot))?.EntityNameSnapshot;

            if (!string.IsNullOrWhiteSpace(entityName))
            {
                var linkedIds = new HashSet<Guid>(playbookEntries.Select(e => e.Id));
                var entityMatched = playbookResult.Value.Where(e =>
                    e.SignatureHashSnapshot is null
                    && e.NamespaceId == namespaceId
                    && !linkedIds.Contains(e.Id)
                    && EntityScopedEvidenceProposalKinds.Contains(e.ProposalKind)
                    && ExtractProposalEntityNames(e.ProposalKind, e.ProposalJson).Contains(entityName, StringComparer.Ordinal));
                playbookEntries = playbookEntries.Concat(entityMatched)
                    .OrderByDescending(e => e.ProposedAt)
                    .ToList();
            }
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

    /// <summary>
    /// Finds the Fingerprint-space signature that represents the same failure as a Cluster-space
    /// hash, matching on the two attributes both identity vocabularies derive from and agree on:
    /// the entity the message dead-lettered in, and the dominant dead-letter reason. Requires a
    /// single unambiguous match — resolving to the wrong sibling would attach one failure's
    /// recovery history to another's incident page, which is worse than reporting not-found.
    /// </summary>
    private async Task<string?> ResolveEquivalentFingerprintHashAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken)
    {
        // Precise path first: if a SignatureReplayJob was actually run against this exact
        // Cluster-space hash, we know exactly which DlqMessage rows it replayed — trace those to
        // the RecoveryLedgerEntries they produced and read the Fingerprint hash off the entries
        // themselves. This sidesteps the entity+reason heuristic below entirely, so it stays
        // correct even when several Fingerprint signatures collide on entity+reason (e.g. many
        // manual Test DLQ batches against the same queue on the same day — the exact case that
        // made the heuristic refuse to guess).
        var preciseHash = await ResolveFingerprintHashFromReplayJobAsync(
            ownerId, namespaceId, signatureHash, cancellationToken).ConfigureAwait(false);
        if (preciseHash is not null)
        {
            return preciseHash;
        }

        var persisted = await _signatureLookupService.GetByHashAsync(
            ownerId, namespaceId, signatureHash, cancellationToken).ConfigureAwait(false);
        if (persisted is null || persisted.HashKind == SignatureHashKind.Fingerprint)
        {
            // Already fingerprint-space (the exact-match lookup above already tried it), or
            // genuinely never observed — nothing to resolve.
            return null;
        }

        var persistedEntity = ExtractTermValue(persisted.TopTermsJson, "entity:");
        if (string.IsNullOrWhiteSpace(persistedEntity))
            return null;

        var fingerprintSiblings = await _signatureLookupService.GetAllForNamespaceAsync(
            ownerId, namespaceId, SignatureHashKind.Fingerprint, cancellationToken).ConfigureAwait(false);

        var matches = fingerprintSiblings
            .Where(s =>
                string.Equals(ExtractTermValue(s.TopTermsJson, "entity:"), persistedEntity, StringComparison.Ordinal)
                && string.Equals(s.DominantDeadletterReason, persisted.DominantDeadletterReason, StringComparison.Ordinal))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0].SignatureHash : null;
    }

    /// <summary>
    /// Finds the most recently completed <see cref="SignatureReplayJob"/> launched against
    /// <paramref name="signatureHash"/> (a Cluster-space hash), and reads the Fingerprint-space
    /// hash off the <see cref="RecoveryLedgerEntry"/> rows its own snapshotted message IDs
    /// produced — see <c>SignatureReplayExecutor.ResolveTrustSignatureHashAsync</c>, which always
    /// writes entries in Fingerprint space regardless of which space the job was launched from.
    /// Returns null (never guesses) unless every matching entry agrees on a single hash.
    /// </summary>
    private async Task<string?> ResolveFingerprintHashFromReplayJobAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken)
    {
        var job = await _dbContext.SignatureReplayJobs
            .AsNoTracking()
            .Where(j => j.OwnerId == ownerId && j.NamespaceId == namespaceId
                && j.SignatureHash == signatureHash && j.Status == BulkOperationStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            return null;
        }

        List<long>? messageIds;
        try
        {
            messageIds = System.Text.Json.JsonSerializer.Deserialize<List<long>>(job.MessageIdsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (messageIds is null || messageIds.Count == 0)
        {
            return null;
        }

        var candidateHashes = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.DlqMessageId != null
                && messageIds.Contains(e.DlqMessageId!.Value) && e.SignatureHashSnapshot != null)
            .Select(e => e.SignatureHashSnapshot!)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidateHashes.Count == 1 ? candidateHashes[0] : null;
    }

    /// <summary>
    /// Reads the entity name(s) a playbook proposal concerns straight out of its
    /// <see cref="PlaybookEntry.ProposalJson"/> — <c>AnomalyFlag</c>/<c>DriftFinding</c> carry a
    /// single top-level <c>EntityName</c> (see <c>AnomalyDetectionWorker</c>/
    /// <c>DriftDetectionWorker</c>'s <c>ProposePlaybookEntryAsync</c>); <c>CorrelationHypothesis</c>
    /// carries several under <c>Members[].EntityName</c> (see
    /// <c>CorrelationDetectionWorker.ProposePlaybookEntryAsync</c>). Never throws on malformed or
    /// unrecognized JSON — returns no matches instead.
    /// </summary>
    private static List<string> ExtractProposalEntityNames(string proposalKind, string proposalJson)
    {
        var names = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(proposalJson);
            var root = doc.RootElement;

            if (proposalKind == "CorrelationHypothesis")
            {
                if (root.TryGetProperty("Members", out var members) && members.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var member in members.EnumerateArray())
                    {
                        if (member.TryGetProperty("EntityName", out var memberEntity)
                            && memberEntity.ValueKind == System.Text.Json.JsonValueKind.String
                            && memberEntity.GetString() is { Length: > 0 } memberName)
                        {
                            names.Add(memberName);
                        }
                    }
                }
            }
            else if (root.TryGetProperty("EntityName", out var entityProp)
                && entityProp.ValueKind == System.Text.Json.JsonValueKind.String
                && entityProp.GetString() is { Length: > 0 } entityName)
            {
                names.Add(entityName);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed proposal JSON — nothing to match on.
        }

        return names;
    }

    /// <summary>Reads the value of a <c>prefix:value</c> entry out of a persisted top-terms array.</summary>
    private static string? ExtractTermValue(string topTermsJson, string prefix)
    {
        List<string>? terms;
        try
        {
            terms = System.Text.Json.JsonSerializer.Deserialize<List<string>>(topTermsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        var term = terms?.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal));
        return term?[prefix.Length..];
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
