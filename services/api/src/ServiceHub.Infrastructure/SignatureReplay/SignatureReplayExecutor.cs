using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// <inheritdoc cref="ISignatureReplayExecutor"/>
/// </summary>
/// <remarks>
/// Every message is processed through <see cref="IMessageOperationsService"/> — the same call
/// <see cref="BulkOperations.BulkOperationExecutor"/> and single-message replay already use — so
/// this class contributes no new replay behavior, only the loop, progress persistence, and
/// cancellation around it. Mirrors <see cref="BulkOperationExecutor"/>'s load/guard/save shape.
/// </remarks>
public sealed class SignatureReplayExecutor : ISignatureReplayExecutor
{
    private const int MaxFailureSampleSize = 20;

    // Persisted after every message (not batched) so a process crash mid-batch never leaves a
    // message's just-completed Replayed/ReplayFailed outcome unpersisted behind its already-
    // committed Replaying claim — see RC1 review H2.
    private const int SaveProgressEveryNMessages = 1;

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IMessageOperationsService _messageOperationsService;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEligibilityGate _eligibilityGate;
    private readonly ILogger<SignatureReplayExecutor> _logger;

    /// <summary>Initialises a new instance of <see cref="SignatureReplayExecutor"/>.</summary>
    public SignatureReplayExecutor(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IMessageOperationsService messageOperationsService,
        IRecoveryLedger recoveryLedger,
        IRecoveryEligibilityGate eligibilityGate,
        ILogger<SignatureReplayExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _eligibilityGate = eligibilityGate ?? throw new ArgumentNullException(nameof(eligibilityGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The recovery-ledger context threaded through <see cref="ProcessMessageAsync"/> for every
    /// message in one job — one <see cref="RecoveryOperation"/> per job, opened once in
    /// <see cref="RunAsync"/>, not per message.
    /// </summary>
    private sealed record RecoveryContext(string OwnerId, Guid OperationId, RecoveryActor Actor, Namespace Namespace, string SignatureHash);

    /// <inheritdoc />
    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // Loaded with CancellationToken.None: even if cancellation was requested before this
        // call started, we still need to load and terminate the job cleanly rather than throw.
        var job = await _dbContext.SignatureReplayJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null)
        {
            _logger.LogWarning("Signature replay job {JobId} not found at execution time", jobId);
            return;
        }

        if (job.Status != BulkOperationStatus.Pending)
        {
            _logger.LogWarning(
                "Signature replay job {JobId} is not Pending (status={Status}) — skipping duplicate dequeue",
                jobId, job.Status);
            return;
        }

        if (job.CancellationRequestedAt.HasValue)
        {
            job.Status = BulkOperationStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            return;
        }

        job.Status = BulkOperationStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        try
        {
            await RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            job.Status = BulkOperationStatus.Cancelled;
            _logger.LogInformation(
                "Signature replay job {JobId} cancelled after processing {Processed}/{Total} message(s)",
                jobId, job.ProcessedCount, job.TotalMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature replay job {JobId} failed unexpectedly", jobId);
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = ex.Message;
        }
        finally
        {
            job.CompletedAt = DateTimeOffset.UtcNow;
            if (job.Status == BulkOperationStatus.Running)
            {
                job.Status = job.FailureCount > 0 || job.SkippedCount > 0
                    ? BulkOperationStatus.CompletedWithErrors
                    : BulkOperationStatus.Completed;
            }

            await SaveChangesTolerantOfStaleMessagesAsync();
        }
    }

    /// <summary>
    /// Saves pending changes, tolerating a stale <see cref="DlqMessage"/> left dirty by a claim
    /// that lost the race with cancellation. <see cref="ProcessMessageAsync"/> claims a message
    /// via <c>SaveChangesAsync(cancellationToken)</c> — if cancellation fires mid-save, that
    /// throws <see cref="OperationCanceledException"/> (not
    /// <see cref="DbUpdateConcurrencyException"/>, so <see cref="ProcessMessageAsync"/>'s own
    /// concurrency handling never runs) and leaves the message entity dirty with a now-stale
    /// concurrency token. If another writer (e.g. <c>DlqMonitorWorker</c>'s reconciliation, or —
    /// as seen live — a routine scan racing an unrelated earlier message in the same batch) has
    /// since touched that row, the next save's own concurrency check on the leftover entry
    /// fails. Called both after every message (so one stray conflict can never abort the rest
    /// of the batch, matching <see cref="ProcessMessageAsync"/>'s own no-abort contract for
    /// per-message races) and from the <c>finally</c> block (so a conflict can never block the
    /// job's terminal status from being saved, which otherwise leaves the job stuck reporting
    /// Running forever).
    /// </summary>
    private async Task SaveChangesTolerantOfStaleMessagesAsync()
    {
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                await entry.ReloadAsync(CancellationToken.None);
            }

            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task RunAsync(SignatureReplayJob job, CancellationToken cancellationToken)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(job.NamespaceId, cancellationToken);
        if (nsResult.IsFailure)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = $"Namespace no longer exists: {nsResult.Error.Message}";
            return;
        }

        var ns = nsResult.Value;

        // Re-check the same guard StartAsync validated — defensive against the namespace's
        // environment changing between job creation and the worker picking it up. Mirrors
        // BulkOperationExecutor.RunAsync's execution-time re-check.
        if (ns.Environment == EnvironmentType.Prod)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = "Namespace is now Production — signature replay blocked at execution time.";
            return;
        }

        var messageIds = JsonSerializer.Deserialize<List<long>>(job.MessageIdsJson) ?? [];

        var messages = await _dbContext.DlqMessages
            .Where(m => messageIds.Contains(m.Id) && m.OwnerId == job.OwnerId)
            .OrderBy(m => m.DetectedAtUtc)
            .ToListAsync(cancellationToken);

        // Execution mechanism is not the actor of record (roadmap §29.10) — see
        // BulkOperationExecutor.RunAsync's identical comment. The requester's real actor was
        // resolved and persisted at job-creation time (SignatureReplayService.StartAsync).
        var actor = new RecoveryActor(job.RequestedByIdentity, job.RequestedByActorKind, job.RequestedByScopes);

        var operationResult = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = job.OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.SignatureJob,
            Actor = actor,
            // SourceJobId is long? (matching AutoReplayRule.Id's type) but SignatureReplayJob.Id
            // is a Guid — same type-mismatch limitation as BulkOperationExecutor; the job's id is
            // embedded in ScopeDescription instead.
            NamespaceId = job.NamespaceId,
            NamespaceNameSnapshot = job.NamespaceDisplayName,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            ScopeDescription = $"signature replay job {job.Id}; signatureHash={job.SignatureHash}",
            CorrelationId = null,
            TargetCount = messages.Count,
        }, cancellationToken);

        if (operationResult.IsFailure)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = $"Failed to open recovery ledger operation: {operationResult.Error.Message}";
            return;
        }

        var recovery = new RecoveryContext(job.OwnerId, operationResult.Value.Id, actor, ns, job.SignatureHash);

        var failureSample = new List<BulkOperationFailureSample>();
        var sinceLastSave = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (outcome, reason, reasonCategory) = await ProcessMessageAsync(message, recovery, cancellationToken);
            job.ProcessedCount++;

            switch (outcome)
            {
                case MessageOutcome.Success:
                    job.SuccessCount++;
                    break;
                case MessageOutcome.Failure:
                    job.FailureCount++;
                    AddToSample(failureSample, message, reason!, reasonCategory);
                    break;
                case MessageOutcome.Skipped:
                    job.SkippedCount++;
                    AddToSample(failureSample, message, reason!, reasonCategory);
                    break;
            }

            if (++sinceLastSave >= SaveProgressEveryNMessages)
            {
                job.FailureSampleJson = failureSample.Count > 0 ? JsonSerializer.Serialize(failureSample) : null;
                await SaveChangesTolerantOfStaleMessagesAsync();
                sinceLastSave = 0;
            }
        }

        job.FailureSampleJson = failureSample.Count > 0 ? JsonSerializer.Serialize(failureSample) : null;
    }

    private async Task<(MessageOutcome Outcome, string? Reason, string? ReasonCategory)> ProcessMessageAsync(
        DlqMessage message, RecoveryContext recovery, CancellationToken cancellationToken)
    {
        // A message already moved on (e.g. replayed manually between job creation and
        // execution) is skipped rather than re-attempted — the filter matched it at creation
        // time, but its current status may no longer reflect that. Same eligibility rule
        // BulkOperationExecutor.ProcessMessageAsync applies.
        if (message.Status != DlqMessageStatus.Active && message.Status != DlqMessageStatus.ReplayFailed)
        {
            return (MessageOutcome.Skipped, $"Message status is now '{message.Status}', no longer eligible for replay", null);
        }

        var (entityName, subscriptionName) = BulkOperationExecutor.ResolveEntityAndSubscription(message);

        var decision = await _eligibilityGate.EvaluateAsync(
            new RecoveryEligibilityRequest(
                recovery.OwnerId, RecoveryOperationKind.Replay, recovery.Actor.Kind, RecoveryTrigger.SignatureJob,
                recovery.Namespace.Id, message.EntityName, message.BodyHash, recovery.SignatureHash,
                recovery.Namespace.Environment, Provider: recovery.Namespace.Provider),
            cancellationToken);

        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            await RecordDeclinedAsync(message, recovery, entityName, decision, cancellationToken);
            return (MessageOutcome.Skipped,
                $"Blocked by the Eligibility Gate ({decision.ReasonCode}) — escalate for manual review", null);
        }

        // Claim the message via optimistic concurrency (Status is a concurrency token — see
        // DlqDbContext.ConfigureDlqMessage) before calling the live provider, so a worker that
        // loses the race never sends a duplicate — not just avoids a duplicate DB row. A
        // losing SaveChangesAsync throws DbUpdateConcurrencyException here, before
        // ReplayMessageAsync is ever invoked.
        message.Status = DlqMessageStatus.Replaying;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(message).ReloadAsync(cancellationToken);
            return (MessageOutcome.Skipped, "Message was claimed by another concurrent replay — skipped", null);
        }

        // CancellationToken.None from here through the provider call: the claim above is already
        // committed, so this message must run to completion — same guarantee RulesController.
        // ReplayAll, AutoReplayExecutor, and BulkOperationExecutor already enforce.
        var beginResult = await _recoveryLedger.BeginEntryAsync(
            RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                message, recovery.Namespace, recovery.OperationId, recovery.OwnerId, recovery.Actor, entityName,
                signatureHashSnapshot: recovery.SignatureHash),
            CancellationToken.None);

        if (beginResult.IsFailure)
        {
            // No message movement without ledger coverage: release the claim so a retry can pick
            // the message up again rather than call the provider unrecorded.
            message.Status = DlqMessageStatus.Active;
            return (MessageOutcome.Skipped, $"Recovery ledger error: {beginResult.Error.Message}", null);
        }

        var entry = beginResult.Value;

        if (decision.ReasonCode is not null)
        {
            await RecordRecurrenceContextAsync(entry.Id, recovery, decision, CancellationToken.None);
        }

        var result = await _messageOperationsService.ReplayMessageAsync(
            message.NamespaceId, entityName, subscriptionName, message.SequenceNumber, entry.Id, CancellationToken.None);

        // AWS.SQS.ReplayAmbiguous (send to source succeeded, delete from DLQ failed) routes to
        // Unknown rather than Rejected: the message is genuinely duplicated-if-retried, not
        // safely retriable — see AwsMessageReceiver.ReplayMessageAsync. Same distinction
        // ReplayFailureClassifier makes as AmbiguousOutcome, kept as two separate checks here
        // since RecoveryExecutionOutcome's ledger semantics and ReplayFailureReason's UI-facing
        // taxonomy are deliberately independent concepts that happen to agree on this one case.
        var executionOutcome = result.IsSuccess
            ? RecoveryExecutionOutcome.Accepted
            : result.Error.Code == "AWS.SQS.ReplayAmbiguous"
                ? RecoveryExecutionOutcome.Unknown
                : RecoveryExecutionOutcome.Rejected;
        var failureReasonCategory = result.IsSuccess ? null : ReplayFailureClassifier.Classify(result.Error).ToString();

        // CancellationToken.None: the provider call above already happened, so this outcome must
        // be recorded even if cancellation was requested in the meantime — same reasoning as the
        // final DlqMessage save below.
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = recovery.OwnerId,
            Actor = recovery.Actor,
            Outcome = executionOutcome,
            ProviderDetailJson = result.IsSuccess ? null : result.Error.Message,
            RecoveryMarker = result.IsSuccess && result.Value ? entry.Id.ToString() : null,
            MarkerApplied = result.IsSuccess && result.Value,
        }, CancellationToken.None);

        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = message.Id,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "signature-replay",
            ReplayStrategy = "original-entity",
            ReplayedToEntity = entityName,
            OutcomeStatus = result.IsSuccess ? "Success" : "Failed",
            ErrorDetails = result.IsSuccess ? null : result.Error.Message,
        });

        if (result.IsSuccess)
        {
            message.Status = DlqMessageStatus.Replayed;
            message.ReplayedAt = DateTimeOffset.UtcNow;
            message.ReplaySuccess = true;
        }
        else
        {
            message.Status = DlqMessageStatus.ReplayFailed;
            message.ReplaySuccess = false;
        }

        // Same concurrency token as the claim above, but the other writer here is typically
        // DlqMonitorService's reconciliation loop, not another replay worker: replaying this
        // message can empty the live DLQ, and a scan landing mid-batch marks every remaining
        // "Active" row for the entity Replayed — including ones this job has since moved past
        // Active. That's a race on bookkeeping, not on the replay itself (which already
        // happened via ReplayMessageAsync above), so a losing save here must not fail the
        // message outcome or abort the rest of the batch — see RunAsync's per-message loop.
        // CancellationToken.None, like the job-progress save in RunAsync: the provider call
        // above already happened, so this outcome must be persisted even if cancellation was
        // requested in the meantime — dropping it here would leave the message stuck
        // "Replaying" despite having actually been replayed.
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(message).ReloadAsync(CancellationToken.None);
        }

        return result.IsSuccess
            ? (MessageOutcome.Success, null, null)
            : (MessageOutcome.Failure, result.Error.Message, failureReasonCategory);
    }

    /// <summary>
    /// Records a truthful <c>Declined</c> ledger entry for an Eligibility Gate verdict that
    /// blocked this message (roadmap §9.3) — never called for a rate limit, since signature
    /// replay jobs have no per-rule rate-limit concept, so every non-<c>Allow</c> verdict reaching
    /// this executor is a real safety escalation. Best-effort: a ledger-write failure never
    /// changes the underlying skip decision (roadmap §18).
    /// </summary>
    private async Task RecordDeclinedAsync(
        DlqMessage message, RecoveryContext recovery, string entityName, EligibilityDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recoveryLedger.RecordDeclinedAsync(
                RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                    message, recovery.Namespace, recovery.OperationId, recovery.OwnerId, recovery.Actor, entityName,
                    signatureHashSnapshot: recovery.SignatureHash),
                decision.ReasonCode ?? "ELIGIBILITY_GATE_DENIED",
                JsonSerializer.Serialize(new { reasonCode = decision.ReasonCode, matchedCount = decision.MatchedCount }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record Declined ledger entry for message {MessageId}",
                LogRedactor.SanitiseForLog(message.MessageId));
        }
    }

    /// <summary>
    /// Records a human actor's recurrence-cap-reached-but-allowed attempt as observability
    /// evidence (roadmap §9.4.1). Best-effort: a ledger-write failure never blocks the recovery
    /// attempt itself, matching <see cref="RecordDeclinedAsync"/>'s same guarantee.
    /// </summary>
    private async Task RecordRecurrenceContextAsync(
        Guid entryId, RecoveryContext recovery, EligibilityDecision decision, CancellationToken cancellationToken)
    {
        try
        {
            await _recoveryLedger.RecordRecurrenceContextAsync(
                entryId, recovery.OwnerId, recovery.Actor, decision.ReasonCode!, decision.MatchedCount,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record RecurrenceCapObserved ledger event for entry {EntryId}", entryId);
        }
    }

    private static void AddToSample(
        List<BulkOperationFailureSample> sample, DlqMessage message, string reason, string? reasonCategory)
    {
        if (sample.Count >= MaxFailureSampleSize)
            return;

        sample.Add(new BulkOperationFailureSample(message.MessageId, message.EntityName, reason, reasonCategory));
    }

    private enum MessageOutcome
    {
        Success,
        Failure,
        Skipped,
    }
}
