using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Executes the replay action for auto-replay rules.
/// Handles the recurrence-lineage safety cap, rate limiting, provider-routed replay via
/// <see cref="IMessageOperationsService"/>, replay-history persistence, and DLQ message status
/// updates.
/// </summary>
public sealed class AutoReplayExecutor : IAutoReplayExecutor
{
    private const int DefaultFleetReplayVelocityCapPerHour = 500;

    // Roadmap "same engine, second application" (item 14, Recover side): a computed plan that
    // predicate 5 escalates purely for lack of earned autonomy — not a recurrence-cap safety
    // stop — is exactly the Recover pillar's L2 "Recommend" candidate, proposed into the Playbook
    // Ledger for human review the same way AnomalyDetectionWorker/DriftDetectionWorker propose
    // AnomalyFlag/DriftFinding. Never itself authorizes anything; see PlaybookEntry's doc remarks.
    private static readonly TimeSpan ReplayPlanProposalExpiry = TimeSpan.FromDays(7);
    private static readonly PlaybookActor ReplayPlanProposer = new("System:AutoReplayExecutor", PlaybookActorKind.System);

    private readonly DlqDbContext _dbContext;
    private readonly IMessageOperationsService _messageOperations;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEligibilityGate _eligibilityGate;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly IFailureFeatureExtractor _featureExtractor;
    private readonly IFailureFingerprintBuilder _fingerprintBuilder;
    private readonly ILogger<AutoReplayExecutor> _logger;
    private readonly int _fleetReplayVelocityCapPerHour;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoReplayExecutor"/> class. Reads
    /// <c>RecoveryEvidence:FleetReplayVelocityCapPerHour</c> from <paramref name="configuration"/>
    /// (default 500, clamped to [1, 100000]).
    /// </summary>
    public AutoReplayExecutor(
        DlqDbContext dbContext,
        IMessageOperationsService messageOperations,
        IRecoveryLedger recoveryLedger,
        IRecoveryEligibilityGate eligibilityGate,
        IPlaybookLedger playbookLedger,
        IFailureFeatureExtractor featureExtractor,
        IFailureFingerprintBuilder fingerprintBuilder,
        IConfiguration configuration,
        ILogger<AutoReplayExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _messageOperations = messageOperations ?? throw new ArgumentNullException(nameof(messageOperations));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _eligibilityGate = eligibilityGate ?? throw new ArgumentNullException(nameof(eligibilityGate));
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _featureExtractor = featureExtractor ?? throw new ArgumentNullException(nameof(featureExtractor));
        _fingerprintBuilder = fingerprintBuilder ?? throw new ArgumentNullException(nameof(fingerprintBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _fleetReplayVelocityCapPerHour = Math.Clamp(
            configuration.GetValue("RecoveryEvidence:FleetReplayVelocityCapPerHour", DefaultFleetReplayVelocityCapPerHour),
            1, 100_000);
    }

    /// <inheritdoc />
    public async Task<Result<string>> ExecuteAsync(
        DlqMessage message,
        AutoReplayRule rule,
        RuleAction action,
        Namespace ns,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing auto-replay rule {RuleId}/{RuleName} on message {MessageId} (DLQ record {DlqId})",
            rule.Id, LogRedactor.SanitiseForLog(rule.Name), LogRedactor.SanitiseForLog(message.MessageId), message.Id);

        // Determine target entity
        string entityName;
        string? subscriptionName = null;

        if (!string.IsNullOrEmpty(action.TargetEntity))
        {
            entityName = action.TargetEntity;
        }
        else if (message.EntityType == ServiceBusEntityType.Subscription && message.TopicName is not null)
        {
            entityName = message.TopicName;
            // EntityName stores full path: "topicName/subscriptions/subName"
            // Extract just the subscription name for the Service Bus receiver
            var prefix = $"{message.TopicName}/subscriptions/";
            subscriptionName = message.EntityName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? message.EntityName[prefix.Length..]
                : message.EntityName;
        }
        else
        {
            entityName = message.EntityName;
        }

        var (decision, signatureHash) = await EvaluateEligibilityAsync(message, rule, ns, cancellationToken);

        if (decision.ReasonCode == RecoveryEligibilityGate.ReasonRateLimited)
        {
            _logger.LogWarning(
                "Rule {RuleId} exceeded rate limit ({Max}/hour), skipping",
                rule.Id, rule.MaxReplaysPerHour);
            return Result<string>.Failure(
                Error.Validation("Rule.RateLimited", $"Rule '{rule.Name}' has exceeded {rule.MaxReplaysPerHour} replays/hour"));
        }

        if (decision.ReasonCode == RecoveryEligibilityGate.ReasonFleetRateLimited)
        {
            _logger.LogWarning(
                "Owner {OwnerId} exceeded fleet-wide replay velocity cap ({Max}/hour) via rule {RuleId}, skipping",
                rule.OwnerId, _fleetReplayVelocityCapPerHour, rule.Id);
            return Result<string>.Failure(
                Error.Validation("Rule.FleetRateLimited",
                    $"Owner has exceeded the fleet-wide {_fleetReplayVelocityCapPerHour} replays/hour cap across all auto-replay rules"));
        }

        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            _logger.LogWarning(
                "Auto-replay for message {MessageId} blocked by recurrence-lineage cap ({MatchedCount} prior matching attempts, reason {Reason})",
                LogRedactor.SanitiseForLog(message.MessageId), decision.MatchedCount, decision.ReasonCode);

            var declineActor = ActorIdentityResolver.ResolveAutomationActor("Rule", rule.Id.ToString(), rule.Name);
            var declineRequest = RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                message, ns, operationId, rule.OwnerId, declineActor, entityName,
                signatureHashSnapshot: signatureHash);

            try
            {
                await _recoveryLedger.RecordDeclinedAsync(
                    declineRequest,
                    decision.ReasonCode ?? "ELIGIBILITY_GATE_DENIED",
                    JsonSerializer.Serialize(new { reasonCode = decision.ReasonCode, matchedCount = decision.MatchedCount }),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // A ledger-write failure must not change the skip decision (roadmap §26 Security
                // note) — the replay stays blocked either way; this is best-effort evidence.
                _logger.LogError(ex,
                    "Failed to record Declined ledger entry for message {MessageId}",
                    LogRedactor.SanitiseForLog(message.MessageId));
            }

            // Only the "hasn't earned it yet" reason is a Recommend candidate — the
            // recurrence-lineage cap is a safety stop, not a plan awaiting trust, so it gets no
            // Playbook proposal.
            if (decision.ReasonCode == RecoveryEligibilityGate.ReasonAutonomyGrantInsufficient)
            {
                try
                {
                    await ProposeReplayPlanAsync(message, rule, ns, entityName, signatureHash, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Same best-effort treatment as the RecordDeclinedAsync call above — a
                    // Playbook Ledger write failure must never change the skip decision.
                    _logger.LogError(ex,
                        "Failed to propose Playbook Ledger ReplayPlan entry for message {MessageId}",
                        LogRedactor.SanitiseForLog(message.MessageId));
                }
            }

            return Result<string>.Failure(Error.Validation(
                "AutoReplay.RecurrenceCapExceeded",
                $"Message has {decision.MatchedCount} prior matching automatic-replay attempts on this lineage " +
                $"(cap {RecoveryEligibilityGate.RecurrenceLineageCap}); escalate for manual review."));
        }

        // Claim the message via optimistic concurrency (Status is a concurrency token — see
        // DlqDbContext.ConfigureDlqMessage) before calling the live provider, so a worker that
        // loses the race against bulk-replay or signature-replay never sends a duplicate — not
        // just avoids a duplicate DB row. A losing SaveChangesAsync throws
        // DbUpdateConcurrencyException here, before ReplayMessageAsync is ever invoked. This
        // also doubles as the eligibility re-check bulk/signature replay already do explicitly.
        message.Status = DlqMessageStatus.Replaying;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(message).ReloadAsync(cancellationToken);
            _logger.LogInformation(
                "Auto-replay for message {MessageId} skipped — claimed by another concurrent replay",
                LogRedactor.SanitiseForLog(message.MessageId));
            return Result<string>.Failure(
                Error.Conflict("AutoReplay.ConcurrentReplay", "Message was claimed by another concurrent replay worker"));
        }

        var actor = ActorIdentityResolver.ResolveAutomationActor("Rule", rule.Id.ToString(), rule.Name);

        var beginResult = await _recoveryLedger.BeginEntryAsync(
            RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                message, ns, operationId, rule.OwnerId, actor, entityName,
                signatureHashSnapshot: signatureHash),
            cancellationToken);

        if (beginResult.IsFailure)
        {
            // No message movement without ledger coverage: release the claim so a retry can pick
            // the message up again rather than call the provider unrecorded.
            message.Status = DlqMessageStatus.Active;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _dbContext.Entry(message).ReloadAsync(cancellationToken);
            }

            return Result<string>.Failure(Error.Internal(
                "AutoReplay.LedgerError", $"Failed to open recovery ledger entry: {beginResult.Error.Message}"));
        }

        var entry = beginResult.Value;

        // Execute the replay
        try
        {
            var replayResult = await _messageOperations.ReplayMessageAsync(
                message.NamespaceId, entityName, subscriptionName, message.SequenceNumber, entry.Id, cancellationToken);

            // AWS.SQS.ReplayAmbiguous (send to source succeeded, delete from DLQ failed) routes to
            // Unknown rather than Rejected: the message is genuinely duplicated-if-retried, not
            // safely retriable — see AwsMessageReceiver.ReplayMessageAsync.
            var executionOutcome = replayResult.IsSuccess
                ? RecoveryExecutionOutcome.Accepted
                : replayResult.Error.Code == "AWS.SQS.ReplayAmbiguous"
                    ? RecoveryExecutionOutcome.Unknown
                    : RecoveryExecutionOutcome.Rejected;

            // CancellationToken.None: the provider call above already happened, so this outcome
            // must be recorded even if cancellation was requested in the meantime.
            await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = rule.OwnerId,
                Actor = actor,
                Outcome = executionOutcome,
                ProviderDetailJson = replayResult.IsSuccess ? null : replayResult.Error.Message,
                RecoveryMarker = replayResult.IsSuccess && replayResult.Value ? entry.Id.ToString() : null,
                MarkerApplied = replayResult.IsSuccess && replayResult.Value,
            }, CancellationToken.None);

            var outcome = replayResult.IsSuccess ? "Success" : "Failed";

            // Record replay history
            var history = new ReplayHistory
            {
                DlqMessageId = message.Id,
                RuleId = rule.Id,
                ReplayedAt = DateTimeOffset.UtcNow,
                ReplayedBy = $"auto-rule:{rule.Name}",
                ReplayStrategy = action.TargetEntity is not null ? "alternate-entity" : "original-entity",
                ReplayedToEntity = entityName,
                OutcomeStatus = outcome,
                ErrorDetails = replayResult.IsFailure ? replayResult.Error.Message : null,
            };

            _dbContext.ReplayHistories.Add(history);

            // Update message status
            if (replayResult.IsSuccess)
            {
                message.Status = DlqMessageStatus.Replayed;
                message.ReplayedAt = DateTimeOffset.UtcNow;
                message.ReplaySuccess = true;
                rule.SuccessCount++;
            }
            else
            {
                message.Status = DlqMessageStatus.ReplayFailed;
                message.ReplaySuccess = false;
            }

            rule.MatchCount++;
            rule.UpdatedAt = DateTimeOffset.UtcNow;

            // CancellationToken.None: RecordExecutionAsync above already committed this outcome to
            // the Recovery Ledger, so the DlqMessage.Status/ReplayHistory record of the same
            // outcome must not be dropped by a cancellation racing this save (e.g. app shutdown
            // firing DlqMonitorWorker's stoppingToken mid-cycle) — the same per-message
            // persistence guarantee RulesController.ReplayAll's replay-all path already enforces.
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation(
                "Auto-replay result for message {MessageId}: {Outcome}",
                LogRedactor.SanitiseForLog(message.MessageId), outcome);

            return replayResult.IsSuccess
                ? Result<string>.Success(outcome)
                : Result<string>.Failure(replayResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-replay failed for message {MessageId}", LogRedactor.SanitiseForLog(message.MessageId));

            // Best-effort: if the try block reached ReplayMessageAsync and RecordExecutionAsync
            // already succeeded, the entry is no longer Executing and this call harmlessly fails
            // with a conflict (RecoveryLedgerService rejects a second RecordExecutionAsync on an
            // already-recorded entry) — it is not double-counted. If the exception happened
            // before that point, this is what actually closes the entry out as Rejected rather
            // than leaving it stranded in Executing until the next restart's reconciliation.
            await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = rule.OwnerId,
                Actor = actor,
                Outcome = RecoveryExecutionOutcome.Rejected,
                ProviderDetailJson = ex.Message,
            }, CancellationToken.None);

            // Record the failure in history
            var history = new ReplayHistory
            {
                DlqMessageId = message.Id,
                RuleId = rule.Id,
                ReplayedAt = DateTimeOffset.UtcNow,
                ReplayedBy = $"auto-rule:{rule.Name}",
                ReplayStrategy = "original-entity",
                ReplayedToEntity = entityName,
                OutcomeStatus = "Error",
                ErrorDetails = ex.Message,
            };

            _dbContext.ReplayHistories.Add(history);
            message.Status = DlqMessageStatus.ReplayFailed;
            message.ReplaySuccess = false;
            rule.MatchCount++;
            rule.UpdatedAt = DateTimeOffset.UtcNow;

            // CancellationToken.None — see the success-path save above for why this must not be
            // cancelled after RecordExecutionAsync has already recorded the ledger outcome.
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            return Result<string>.Failure(Error.Internal("AutoReplay.Exception", ex.Message));
        }
    }

    /// <summary>
    /// Computes the same deterministic failure-signature hash the signature-analysis pipeline uses
    /// (roadmap §4) — <see cref="IFailureFeatureExtractor"/> extracts the message's observable
    /// failure characteristics, <see cref="IFailureFingerprintBuilder"/> hashes them into a stable
    /// identity, independent of provider-generated <see cref="DlqMessage.MessageId"/>/
    /// <see cref="DlqMessage.SequenceNumber"/>. Returns <see langword="null"/> on extraction/hashing
    /// failure so predicate 5 escalates rather than executing with an unidentified signature.
    /// </summary>
    private async Task<string?> ComputeSignatureHashAsync(DlqMessage message, CancellationToken cancellationToken)
    {
        var featuresResult = await _featureExtractor.ExtractAsync(message, cancellationToken);
        if (featuresResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to extract failure features for message {MessageId}: {Error}",
                LogRedactor.SanitiseForLog(message.MessageId), featuresResult.Error.Message);
            return null;
        }

        var fingerprintResult = await _fingerprintBuilder.ComputeAsync(featuresResult.Value, cancellationToken);
        if (fingerprintResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to compute failure fingerprint for message {MessageId}: {Error}",
                LogRedactor.SanitiseForLog(message.MessageId), fingerprintResult.Error.Message);
            return null;
        }

        return fingerprintResult.Value.Hash;
    }

    // Roadmap "same engine, second application" (item 14, Recover side): proposes a Recover-pillar
    // ReplayPlan into the Playbook Ledger for the entity/signature this rule would have replayed,
    // had the signature earned Standing/Unattended autonomy. Joined the same way I3/P2 proposals
    // already are — by (NamespaceId, EntityName) — via BacktestService, plus SignatureHashSnapshot
    // for anything that later wants signature-level precision. Never itself authorizes a replay;
    // a human reviews and dispositions it through the same generic Playbook Ledger API/UI every
    // other pillar's proposals already use.
    private async Task ProposeReplayPlanAsync(
        DlqMessage message,
        AutoReplayRule rule,
        Namespace ns,
        string entityName,
        string? signatureHash,
        CancellationToken cancellationToken)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            EntityName = entityName,
            message.MessageId,
            TargetAction = "Replay",
            RuleId = rule.Id,
            RuleName = rule.Name,
        });
        var evidenceRefJson = JsonSerializer.Serialize(new
        {
            message.MessageId,
            message.SequenceNumber,
            RuleId = rule.Id,
        });

        var result = await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = rule.OwnerId,
            PillarKind = PillarKind.Recover,
            ProposalKind = "ReplayPlan",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = ReplayPlanProposer,
            SignatureHashSnapshot = signatureHash,
            NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            ExpiresAfter = ReplayPlanProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose Playbook Ledger ReplayPlan entry for message {MessageId} in namespace {NamespaceId}: {Error}",
                LogRedactor.SanitiseForLog(message.MessageId), ns.Id, result.Error.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(EligibilityDecision Decision, string? SignatureHash)> EvaluateEligibilityAsync(
        DlqMessage message, AutoReplayRule rule, Namespace ns, CancellationToken cancellationToken = default)
    {
        // Eligibility Gate (roadmap §9/Phase B) — the same gate instance every recovery path
        // shares. Predicate 1 (purge origin) is N/A here (this path only ever replays); predicate
        // 2 (production elevation) is already unreachable here (DlqMonitorWorker never evaluates
        // rules against a Prod namespace); predicate 3 (recurrence cap) is Phase A's original
        // inline check, now generalized into the gate with no behavior change; predicate 4 (rate
        // limit) wraps the existing CanReplayAsync — pre-computed here since only a rule-driven
        // caller has a per-rule limit for the gate to consult; predicate 5 (autonomy lookup) is
        // enforced (roadmap §9.4.3) against the SignatureHash computed below — this call only
        // reaches the provider once its signature has earned AutonomyGrant Standing/Unattended.
        // Evaluated in that fixed order, so if recurrence would already block, the rate-limit
        // query still runs (harmless — read-only) but its result is discarded exactly as before:
        // the gate reports whichever predicate fires first.
        var rateLimitExceeded = !await CanReplayAsync(rule.Id, cancellationToken);
        var fleetRateLimitExceeded = !await CanReplayFleetWideAsync(rule.OwnerId, cancellationToken);
        var signatureHash = await ComputeSignatureHashAsync(message, cancellationToken);

        var decision = await _eligibilityGate.EvaluateAsync(
            new RecoveryEligibilityRequest(
                rule.OwnerId, RecoveryOperationKind.Replay, RecoveryActorKind.Automation, RecoveryTrigger.AutoRule,
                ns.Id, message.EntityName, message.BodyHash, signatureHash, ns.Environment,
                RateLimitExceeded: rateLimitExceeded, Provider: ns.Provider,
                FleetRateLimitExceeded: fleetRateLimitExceeded),
            cancellationToken);

        return (decision, signatureHash);
    }

    /// <inheritdoc />
    public async Task<bool> CanReplayAsync(long ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.AutoReplayRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

        if (rule is null)
            return false;

        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var recentReplays = await _dbContext.ReplayHistories
            .CountAsync(h => h.RuleId == ruleId && h.ReplayedAt >= oneHourAgo, cancellationToken);

        return recentReplays < rule.MaxReplaysPerHour;
    }

    /// <inheritdoc />
    public async Task<bool> CanReplayFleetWideAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var recentFleetReplays = await _dbContext.ReplayHistories
            .Where(h => h.RuleId != null && h.ReplayedAt >= oneHourAgo)
            .Join(
                _dbContext.AutoReplayRules.Where(r => r.OwnerId == ownerId),
                h => h.RuleId,
                r => r.Id,
                (h, _) => h.Id)
            .CountAsync(cancellationToken);

        return recentFleetReplays < _fleetReplayVelocityCapPerHour;
    }
}
