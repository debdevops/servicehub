using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Periodically recomputes Evidence-Derived Trust Scoring (roadmap §8.10, Phase C) for every
/// signature with replay evidence and writes the resulting promotion/demotion to
/// <c>AutonomyGrant</c> via <see cref="IRecoveryLedger.RecordAutonomyGrantTransitionAsync"/>
/// whenever the evidence genuinely earns or forfeits standing (roadmap §8.7, §9.4.3). Still
/// "learning" as recomputed aggregation over the same scheduled-job pattern as
/// <see cref="RecoveryAgeingWorker"/>, not a Learning Engine subsystem (roadmap §13) — every
/// number driving a transition is a deterministic ledger query, never a model output. The
/// Eligibility Gate's predicate 5 (§9.4.3) now enforces the grants this worker writes against
/// <c>AutoReplayExecutor</c>'s computed <c>SignatureHash</c>, so a transition written here has
/// immediate execution-authority effect on the next auto-replay evaluation for that signature.
/// </summary>
/// <remarks>
/// Purely a query-driven sweep over durable ledger state, like <see cref="RecoveryAgeingWorker"/>
/// and <see cref="RecoveryVerificationWorker"/>: a restart loses nothing, because nothing is held
/// in memory between sweeps.
/// </remarks>
public sealed class AutonomyEvaluationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutonomyEvaluationWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    private const int DefaultSweepIntervalSeconds = 3600;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="AutonomyEvaluationWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds</c>
    /// (default 3600, clamped to [60, 86400]).
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public AutonomyEvaluationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AutonomyEvaluationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 86400));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Autonomy Evaluation Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var namespaceRepo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

                var namespacesResult = await namespaceRepo.GetActiveAsync(stoppingToken);
                if (namespacesResult.IsSuccess)
                {
                    var ownerIds = namespacesResult.Value
                        .Select(n => n.OwnerId)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var ownerId in ownerIds)
                    {
                        await SweepOwnerAsync(scope.ServiceProvider, ownerId, stoppingToken);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Autonomy Evaluation Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Autonomy Evaluation Worker sweep cycle");
            }

            try
            {
                await Task.Delay(_sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Autonomy Evaluation Worker stopped");
    }

    /// <summary>Sweeps one owner's signatures with replay evidence. Internal (rather than
    /// private) so tests can drive a single sweep cycle directly instead of waiting on
    /// <see cref="ExecuteAsync"/>'s timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();
        var trustScoring = services.GetRequiredService<IRecoveryTrustScoringService>();

        var signatureHashes = await recoveryLedger.GetDistinctSignatureHashesAsync(
            ownerId, RecoveryOperationKind.Replay, cancellationToken);

        if (signatureHashes.Count == 0)
        {
            return;
        }

        var l4Eligible = 0;
        var l5Eligible = 0;
        var transitionsWritten = 0;

        foreach (var signatureHash in signatureHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await trustScoring.EvaluateAsync(
                ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogDebug(
                    "Skipped trust evaluation for owner {OwnerId} signature {SignatureHash}: {Error}",
                    ownerId, signatureHash, result.Error.Message);
                continue;
            }

            var evidence = result.Value;

            if (evidence.MeetsL4SampleAndRate)
            {
                l4Eligible++;
            }

            if (evidence.MeetsL5SampleAndRate)
            {
                l5Eligible++;
            }

            var currentGrant = await recoveryLedger.GetAutonomyGrantAsync(
                ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken);
            var currentLevel = currentGrant?.CurrentLevel ?? AutonomyLevel.Approve;

            var provider = await recoveryLedger.GetSignatureProviderAsync(ownerId, signatureHash, cancellationToken);
            var canProveDlqAbsence = GetCapabilities(provider).CanProveDlqAbsence;

            var transition = DetermineTransition(currentLevel, evidence, canProveDlqAbsence);
            if (transition is null)
            {
                continue;
            }

            try
            {
                var transitionResult = await recoveryLedger.RecordAutonomyGrantTransitionAsync(
                    ownerId, signatureHash, RecoveryOperationKind.Replay,
                    currentLevel, transition.Value.NewLevel, transition.Value.Reason,
                    BuildEvidenceJson(evidence), cancellationToken);

                if (transitionResult.IsSuccess)
                {
                    transitionsWritten++;
                }
                else
                {
                    _logger.LogWarning(
                        "Autonomy grant transition rejected for owner {OwnerId} signature {SignatureHash}: {Error}",
                        ownerId, signatureHash, transitionResult.Error.Message);
                }
            }
            catch (DbUpdateException ex)
            {
                // A losing concurrent writer (roadmap §9.4.4) — either a first-creation
                // uniqueness race or a mutation-concurrency-token race. Not a caller error: the
                // next sweep re-evaluates this signature from the ledger's current state.
                _logger.LogWarning(ex,
                    "Autonomy grant transition lost a concurrency race for owner {OwnerId} signature {SignatureHash}; " +
                    "will re-evaluate next sweep",
                    ownerId, signatureHash);
            }
        }

        _logger.LogInformation(
            "Autonomy Evaluation Worker: owner {OwnerId} — {SignatureCount} signature(s) with replay evidence, " +
            "{L4Count} meet the L3→L4 sample/rate threshold, {L5Count} meet the L4→L5 sample/rate threshold, " +
            "{TransitionCount} AutonomyGrant transition(s) written this sweep",
            ownerId, signatureHashes.Count, l4Eligible, l5Eligible, transitionsWritten);
    }

    /// <summary>
    /// Decides one signature's next <c>AutonomyGrant</c> transition, if any, from its current
    /// level and freshly computed trust evidence. Never returns a level below L3 (Approve) or
    /// above L5 (Unattended), never skips a tier in one sweep (L3→L4 or L4→L5 only, never
    /// L3→L5), and never promotes into a tier the evidence does not genuinely satisfy (roadmap
    /// §8.9 — "machinery exists" is not "trust is earned").
    /// </summary>
    /// <param name="currentLevel">The signature's current <c>AutonomyGrant</c> level.</param>
    /// <param name="evidence">Freshly computed trust evidence for the signature.</param>
    /// <param name="canProveDlqAbsence">
    /// The signature's provider's <see cref="ProviderCapabilities.CanProveDlqAbsence"/>
    /// (roadmap §14, Phase F). L4/L5 promotion requires it — ServiceHub's own verification must
    /// be trustworthy before granting unattended execution — while L3 and every demotion path
    /// are unaffected, since a human evaluator bears the risk at L3 regardless of provider, and
    /// withdrawing trust is always safe.
    /// </param>
    /// <remarks>
    /// Demotion is intentionally narrower than the full roadmap model this increment: it covers
    /// the two triggers computable from existing aggregate evidence — a verified success rate
    /// that has fallen below the L4 floor (§8.5), and a <c>duplicate_association</c> flag, a
    /// permanent disqualifier for both L4 and L5 (§8.10). The "two consecutive verified
    /// Returned/failure outcomes" fast-demotion path (§7.6, §8.5, §8.6) needs a new
    /// recency-ordered ledger query this increment deliberately does not add — it is the next
    /// increment's work, alongside predicate 5 enforcement, not bundled here. Note §8.6 itself
    /// defines no rate-based demotion trigger for L5 (only duplicate-association and the
    /// deferred consecutive-failure check) — this method's absence of an L5 rate check is
    /// spec-accurate, not an oversight.
    /// </remarks>
    internal static (AutonomyLevel NewLevel, string Reason)? DetermineTransition(
        AutonomyLevel currentLevel, SignatureTrustEvidence evidence, bool canProveDlqAbsence)
    {
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
    /// Maps a signature's provider (roadmap §14) to its <see cref="ProviderCapabilities"/>.
    /// <see langword="null"/> (no ledger entry has a <c>ProviderSnapshot</c> yet, e.g. a very old
    /// record predating that column) fails closed to AWS's/GCP's non-verifying capabilities —
    /// never Azure's — so an unresolvable provider can never itself justify an L4/L5 promotion.
    /// </summary>
    private static ProviderCapabilities GetCapabilities(CloudProviderType? provider) => provider switch
    {
        CloudProviderType.Aws => ProviderCapabilities.Aws,
        CloudProviderType.Gcp => ProviderCapabilities.Gcp,
        CloudProviderType.Azure => ProviderCapabilities.Azure,
        _ => ProviderCapabilities.Aws,
    };

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
}
