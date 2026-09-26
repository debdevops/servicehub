using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Telemetry;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Works out what each failure signature has earned (unit 4.1): recomputes trust from recorded outcomes and moves a
/// signature's <c>AutonomyGrant</c> up or down one level when the evidence genuinely earns or forfeits it.
/// </summary>
/// <remarks>
/// <para>
/// Adapted from 4.0.0's <c>AutonomyEvaluationWorker</c>: the grant sweep and <see cref="DetermineTransition"/> are
/// taken unchanged; its success-rate circuit breaker is not — 4.1.0's rules own their breaker (unit 3.6).
/// </para>
/// <para>
/// <b>What it will never do.</b> Promote past what the evidence shows (L3→L4 needs 10 verified outcomes at ≥95%,
/// L4→L5 30 at ≥99%), skip a level, promote where the cloud cannot prove a dead-letter queue stayed empty, or
/// promote in Production. L3 — a person approves each one — is a <b>floor</b>, not a stage to graduate from: most
/// signatures should stay there. No language model has any say (R3). Every transition is also a hash-chained ledger
/// event, so a grant is never the only record of why.
/// </para>
/// It <b>proposes</b>: a grant is ServiceHub's own bookkeeping; the eligibility gate is what reads it and decides.
/// </remarks>
public sealed class AutonomyEvaluationAgent : IAgent
{
    private const int DefaultSweepIntervalSeconds = 3600;
    private const int DefaultMaxSignatureSweepBatchSize = 1000;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AutonomyEvaluationAgent> _logger;
    private readonly int _maxSignatureSweepBatchSize;

    /// <summary>Creates the agent. Cadence comes from <c>RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds</c>.</summary>
    public AutonomyEvaluationAgent(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<AutonomyEvaluationAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        var interval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds", DefaultSweepIntervalSeconds), 60, 86400));
        _maxSignatureSweepBatchSize = Math.Clamp(
            configuration.GetValue("RecoveryEvidence:MaxSignatureSweepBatchSize", DefaultMaxSignatureSweepBatchSize), 1, 100_000);

        Descriptor = new AgentDescriptor(
            Id: "autonomy-evaluation",
            Name: "Trust Evaluator",
            Purpose: "Counts how often replaying each kind of failure really fixed it, and decides whether that failure has earned automatic replay — or has lost it.",
            Kind: AgentKind.Decide,
            Authority: AgentAuthority.Proposes,
            Cadence: interval,
            Notes: "A person approves every replay until a failure has 10 verified fixes at 95% or better, on a cloud that can prove the queue stayed empty. Never in Production.",
            May: ["Let a failure be replayed without asking once its verified record earns it", "Take that back as soon as the record falls"],
            MayNot: ["Replay anything itself", "Skip a step, or promote in a Production namespace", "Promote where the cloud cannot prove a fix held", "Use AI or guesses — only counted outcomes"],
            LedgerActor: "System:AutonomyEvaluationAgent");
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var namespaces = await scope.ServiceProvider.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
        if (namespaces.IsFailure)
        {
            throw new InvalidOperationException("The list of connected clouds could not be read.");
        }

        var totals = new SweepTotals();
        foreach (var ownerId in namespaces.Value.Select(n => n.OwnerId).Distinct(StringComparer.Ordinal))
        {
            await SweepOwnerAsync(scope.ServiceProvider, ownerId, ct, totals).ConfigureAwait(false);
        }

        if (totals.Signatures == 0)
        {
            return AgentCycleResult.Idle("no replayed failures to evaluate yet");
        }

        var summary = $"evaluated {Count(totals.Signatures, "failure signature")}: {totals.Promoted} promoted, {totals.Demoted} demoted";
        return new AgentCycleResult(totals.Signatures, 0, summary);
    }

    /// <summary>Running totals for one cycle.</summary>
    internal sealed class SweepTotals
    {
        public int Signatures { get; set; }
        public int Promoted { get; set; }
        public int Demoted { get; set; }
    }

    /// <summary>Sweeps one owner's signatures with replay evidence. Internal so tests can drive one sweep (4.0.0's signature).</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken, SweepTotals? totals = null)
    {
        totals ??= new SweepTotals();
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();
        var trustScoring = services.GetRequiredService<IRecoveryTrustScoringService>();
        var metrics = services.GetService<ServiceHubMetrics>();
        var eventBus = services.GetService<IPlatformEventBus>();

        var signatureHashes = await recoveryLedger.GetDistinctSignatureHashesAsync(
            ownerId, RecoveryOperationKind.Replay, _maxSignatureSweepBatchSize, cancellationToken).ConfigureAwait(false);

        if (signatureHashes.Count == _maxSignatureSweepBatchSize)
        {
            _logger.LogWarning(
                "Owner {OwnerId} has at least {Limit} signatures with replay evidence — this sweep evaluated only the first {Limit}",
                ownerId, _maxSignatureSweepBatchSize, _maxSignatureSweepBatchSize);
        }

        foreach (var signatureHash in signatureHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totals.Signatures++;

            // ADR-0010's hard ceiling: nothing above L3 in Production, under any configuration. A grant above the floor
            // (reachable only if a namespace was relabelled Prod after earning one) is taken back down.
            var environment = await recoveryLedger.GetSignatureEnvironmentAsync(ownerId, signatureHash, cancellationToken).ConfigureAwait(false);
            if (environment == EnvironmentType.Prod)
            {
                var prodGrant = await recoveryLedger.GetAutonomyGrantAsync(ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken).ConfigureAwait(false);
                if (prodGrant is { CurrentLevel: > AutonomyLevel.Approve })
                {
                    var demoted = await recoveryLedger.RecordAutonomyGrantTransitionAsync(
                        ownerId, signatureHash, RecoveryOperationKind.Replay, prodGrant.CurrentLevel, AutonomyLevel.Approve,
                        "Production ceiling (ADR-0010): no autonomy above L3 in Prod, under any configuration.",
                        evidenceJson: null, cancellationToken).ConfigureAwait(false);
                    if (demoted.IsSuccess)
                    {
                        totals.Demoted++;
                    }
                }

                continue;
            }

            var result = await trustScoring.EvaluateAsync(ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                continue;
            }

            var evidence = result.Value;
            var currentGrant = await recoveryLedger.GetAutonomyGrantAsync(ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken).ConfigureAwait(false);
            var currentLevel = currentGrant?.CurrentLevel ?? AutonomyLevel.Approve;

            var provider = await recoveryLedger.GetSignatureProviderAsync(ownerId, signatureHash, cancellationToken).ConfigureAwait(false);
            var canProveDlqAbsence = await CanProveDlqAbsenceAsync(
                recoveryLedger, services.GetService<IDlqObserverAttestationService>(), ownerId, signatureHash, provider, cancellationToken).ConfigureAwait(false);

            var transition = DetermineTransition(currentLevel, evidence, canProveDlqAbsence);
            if (transition is null)
            {
                continue;
            }

            try
            {
                var written = await recoveryLedger.RecordAutonomyGrantTransitionAsync(
                    ownerId, signatureHash, RecoveryOperationKind.Replay, currentLevel, transition.Value.NewLevel, transition.Value.Reason,
                    BuildEvidenceJson(evidence), cancellationToken).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    _logger.LogWarning("Autonomy grant transition refused for signature {SignatureHash}: {Error}", signatureHash, written.Error.Message);
                    continue;
                }

                var isPromotion = transition.Value.NewLevel > currentLevel;
                if (isPromotion) totals.Promoted++;
                else totals.Demoted++;
                metrics?.RecordAutonomyTransition(isPromotion ? "promotion" : "demotion", currentLevel.ToString(), transition.Value.NewLevel.ToString());

                if (eventBus is not null)
                {
                    await eventBus.PublishAsync(new PlatformEvent
                    {
                        Source = Descriptor.Id,
                        Category = EventCategories.Autonomy,
                        EventType = EventTypes.AutonomyGrantTransitioned,
                        Severity = isPromotion ? EventSeverity.Info : EventSeverity.Warning,
                        Actor = ownerId,
                        TargetScope = signatureHash,
                        Payload = new AutonomyGrantTransitionedPayload
                        {
                            OwnerId = ownerId, SignatureHash = signatureHash, OperationKind = RecoveryOperationKind.Replay,
                            PreviousLevel = currentLevel, NewLevel = transition.Value.NewLevel, Reason = transition.Value.Reason,
                            TransitionedAtUtc = DateTimeOffset.UtcNow,
                        },
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (DbUpdateException ex)
            {
                // A losing concurrent writer; the next sweep re-evaluates from the ledger's current state.
                _logger.LogWarning(ex, "Autonomy grant transition lost a concurrency race for signature {SignatureHash}; will re-evaluate next sweep", signatureHash);
            }
        }
    }

    /// <summary>
    /// Decides one signature's next transition, if any. Never below L3 or above L5, never skips a tier in one sweep,
    /// never promotes into a tier the evidence does not satisfy, and never promotes where the cloud cannot prove absence
    /// — while demotion is always allowed, because withdrawing trust is always safe. Copied from 4.0.0 unchanged.
    /// </summary>
    internal static (AutonomyLevel NewLevel, string Reason)? DetermineTransition(
        AutonomyLevel currentLevel, SignatureTrustEvidence evidence, bool canProveDlqAbsence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (currentLevel == AutonomyLevel.Approve
            && evidence.MeetsL4SampleAndRate
            && !evidence.UnsafeOutcomePresent
            && !evidence.DuplicateAssociationPresent
            && canProveDlqAbsence)
        {
            return (AutonomyLevel.Standing, FormatPromotionReason(AutonomyLevel.Approve, AutonomyLevel.Standing, evidence));
        }

        if (currentLevel == AutonomyLevel.Standing
            && evidence.MeetsL5SampleAndRate
            && !evidence.UnsafeOutcomePresent
            && !evidence.DuplicateAssociationPresent
            && canProveDlqAbsence)
        {
            return (AutonomyLevel.Unattended, FormatPromotionReason(AutonomyLevel.Standing, AutonomyLevel.Unattended, evidence));
        }

        if (currentLevel is AutonomyLevel.Standing or AutonomyLevel.Unattended && evidence.DuplicateAssociationPresent)
        {
            return (AutonomyLevel.Approve, FormatDuplicateDemotionReason(currentLevel));
        }

        if (currentLevel == AutonomyLevel.Standing && !evidence.MeetsL4SampleAndRate)
        {
            return (AutonomyLevel.Approve, FormatRateDemotionReason(currentLevel, evidence));
        }

        return null;
    }

    /// <summary>
    /// Whether the signature's cloud can prove a replayed message stayed out of the dead-letter queue — natively, or while
    /// its namespace has a live DLQ observer attestation (unit 4.2; none is registered before it). An unknown cloud
    /// cannot prove anything: it fails closed without naming any cloud (R4).
    /// </summary>
    private static async Task<bool> CanProveDlqAbsenceAsync(
        IRecoveryLedger recoveryLedger, IDlqObserverAttestationService? attestationService,
        string ownerId, string signatureHash, CloudProviderType? provider, CancellationToken cancellationToken)
    {
        if (provider is { } known && ProviderCapabilities.For(known).CanProveDlqAbsence)
        {
            return true;
        }

        if (attestationService is null
            || await recoveryLedger.GetSignatureNamespaceIdAsync(ownerId, signatureHash, cancellationToken).ConfigureAwait(false) is not { } nsId)
        {
            return false;
        }

        return await attestationService.IsLiveAsync(ownerId, nsId, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatPromotionReason(AutonomyLevel from, AutonomyLevel to, SignatureTrustEvidence evidence) =>
        $"Promoted {from}→{to}: n={evidence.SampleSize}, verified_success_rate={evidence.VerifiedSuccessRate:P0}, " +
        "unsafe_outcome_count=0, duplicate_association=0 (roadmap §8.10).";

    private static string FormatRateDemotionReason(AutonomyLevel from, SignatureTrustEvidence evidence) =>
        $"Demoted {from}→{AutonomyLevel.Approve}: verified_success_rate {evidence.VerifiedSuccessRate:P0} " +
        $"(n={evidence.SampleSize}) fell below the floor required to hold {from} — non-disableable (roadmap §8.5).";

    private static string FormatDuplicateDemotionReason(AutonomyLevel from) =>
        $"Demoted {from}→{AutonomyLevel.Approve}: an operator flagged a duplicate business effect for this " +
        "signature — a permanent disqualifier until an explicit human act restores eligibility (roadmap §8.10).";

    private static string BuildEvidenceJson(SignatureTrustEvidence evidence) => JsonSerializer.Serialize(new
    {
        sampleSize = evidence.SampleSize,
        verifiedSuccessRate = evidence.VerifiedSuccessRate,
        recoveredCount = evidence.RecoveredCount,
        returnedCount = evidence.ReturnedCount,
        failedCount = evidence.FailedCount,
        unverifiedCount = evidence.UnverifiedCount,
        unsafeOutcomePresent = evidence.UnsafeOutcomePresent,
        duplicateAssociationPresent = evidence.DuplicateAssociationPresent,
    });

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
