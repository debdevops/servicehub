using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.BulkOperations;

/// <summary>
/// <inheritdoc cref="IBulkOperationExecutor"/>
/// </summary>
/// <remarks>
/// Every message is processed through <see cref="IMessageOperationsService"/> — the same
/// provider-neutral facade single-message replay/purge already goes through
/// (<c>MessagesController.ReplayMessage</c>/<c>PurgeMessage</c>) — one call per message.
/// Deliberately sequential, not concurrent: AWS and GCP resolve a message's identity by
/// re-scanning the entity and hashing <c>MessageId</c> per call (see docs/EXTENDING-PROVIDERS.md);
/// concurrent calls against the same entity would multiply redundant re-scans and risk
/// provider throttling for no throughput benefit at the volumes this feature targets
/// (hundreds to low thousands of messages). If a real workload later needs higher throughput,
/// bounded per-entity concurrency is a contained follow-up, not a redesign.
/// </remarks>
public sealed class BulkOperationExecutor : IBulkOperationExecutor
{
    // Persisted after every message (not batched) so a process crash mid-batch never leaves a
    // message's just-completed Replayed/ReplayFailed outcome unpersisted behind its already-
    // committed Replaying claim — see RC1 review H2.
    private const int SaveProgressEveryNMessages = 1;
    private const int MaxFailureSampleSize = 20;
    private const string SubscriptionPathSegment = "/subscriptions/";

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IMessageOperationsService _messageOperationsService;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEligibilityGate _eligibilityGate;
    private readonly IAuditService _auditService;
    private readonly IPlatformEventBus _eventBus;
    private readonly ILogger<BulkOperationExecutor> _logger;

    /// <summary>Initialises a new instance of <see cref="BulkOperationExecutor"/>.</summary>
    public BulkOperationExecutor(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IMessageOperationsService messageOperationsService,
        IRecoveryLedger recoveryLedger,
        IRecoveryEligibilityGate eligibilityGate,
        IAuditService auditService,
        IPlatformEventBus eventBus,
        ILogger<BulkOperationExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _eligibilityGate = eligibilityGate ?? throw new ArgumentNullException(nameof(eligibilityGate));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The recovery-ledger context threaded through <see cref="ProcessMessageAsync"/> for every
    /// message in one job — one <see cref="RecoveryOperation"/> per job, opened once in
    /// <see cref="RunAsync"/>, not per message.
    /// </summary>
    private sealed record RecoveryContext(string OwnerId, Guid OperationId, RecoveryActor Actor, Namespace Namespace);

    /// <inheritdoc />
    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // Loaded with CancellationToken.None: even if cancellation was requested before this
        // call started, we still need to load and terminate the job cleanly rather than throw.
        var job = await _dbContext.BulkOperationJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null)
        {
            _logger.LogWarning("Bulk operation job {JobId} not found at execution time", jobId);
            return;
        }

        if (job.Status != BulkOperationStatus.Pending)
        {
            _logger.LogWarning(
                "Bulk operation job {JobId} is not Pending (status={Status}) — skipping duplicate dequeue",
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
                "Bulk operation job {JobId} cancelled after processing {Processed}/{Total} message(s)",
                jobId, job.ProcessedCount, job.TotalMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk operation job {JobId} failed unexpectedly", jobId);
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
            RecordCompletionAudit(job);
            await PublishCompletionEventAsync(job);
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

    private async Task RunAsync(BulkOperationJob job, CancellationToken cancellationToken)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(job.NamespaceId, cancellationToken);
        if (nsResult.IsFailure)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = $"Namespace no longer exists: {nsResult.Error.Message}";
            return;
        }

        var ns = nsResult.Value;

        // Re-check the same guard CreateJobAsync validated — defensive against the namespace's
        // environment changing between job creation and the worker picking it up.
        if (ns.Environment == EnvironmentType.Prod)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = "Namespace is now Production — bulk operation blocked at execution time.";
            return;
        }

        var messages = await BulkOperationMatching
            .BuildQuery(
                _dbContext, job.OwnerId, job.NamespaceId, job.EntityNameFilter,
                job.StatusFilter, job.CategoryFilter, job.FromFilter, job.ToFilter,
                trackChanges: true)
            .OrderBy(m => m.DetectedAtUtc)
            .ToListAsync(cancellationToken);

        // Execution mechanism is not the actor of record (roadmap §29.10): this job may have been
        // requested by a human through the UI, even though a background worker with no
        // HttpContext performs the actual provider call. The requester's real actor was resolved
        // and persisted at job-creation time (BulkOperationService.CreateJobAsync) — reconstructed
        // here instead of synthesizing an Automation actor, so the Eligibility Gate's predicate 1
        // (purge-origin prohibition) and every ledger write reflect who actually asked for this.
        var actor = new RecoveryActor(job.RequestedByIdentity, job.RequestedByActorKind, job.RequestedByScopes);
        var kind = job.OperationType == BulkOperationType.Replay ? RecoveryOperationKind.Replay : RecoveryOperationKind.Purge;

        var operationResult = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = job.OwnerId,
            Kind = kind,
            Trigger = RecoveryTrigger.BulkJob,
            Actor = actor,
            // BulkOperationJob has no dedicated operator-reason field yet (adding one is a schema
            // change, out of scope for this phase — see Phase 3 plan). The job's own persisted
            // filter is a truthful, if system-derived rather than operator-typed, description of
            // what was targeted, and satisfies the ledger's non-empty-Reason-for-Purge rule
            // without fabricating an operator's words.
            Reason = kind == RecoveryOperationKind.Purge ? BuildFilterDescription(job) : null,
            NamespaceId = job.NamespaceId,
            NamespaceNameSnapshot = job.NamespaceDisplayName,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            // SourceJobId is long? (matching AutoReplayRule.Id's type) but BulkOperationJob.Id is
            // a Guid — no type-compatible way to populate it without a schema change, out of
            // scope this phase. The job's Guid id is embedded in ScopeDescription instead, so the
            // provenance is still recorded, just not as a queryable typed column.
            ScopeDescription = BuildFilterDescription(job),
            CorrelationId = job.CorrelationId,
            TargetCount = messages.Count,
        }, cancellationToken);

        if (operationResult.IsFailure)
        {
            job.Status = BulkOperationStatus.Failed;
            job.ErrorSummary = $"Failed to open recovery ledger operation: {operationResult.Error.Message}";
            return;
        }

        var recovery = new RecoveryContext(job.OwnerId, operationResult.Value.Id, actor, ns);

        var failureSample = new List<BulkOperationFailureSample>();
        var sinceLastSave = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (outcome, reason) = await ProcessMessageAsync(job.OperationType, message, recovery, cancellationToken);
            job.ProcessedCount++;

            switch (outcome)
            {
                case MessageOutcome.Success:
                    job.SuccessCount++;
                    break;
                case MessageOutcome.Failure:
                    job.FailureCount++;
                    AddToSample(failureSample, message, reason!);
                    break;
                case MessageOutcome.Skipped:
                    job.SkippedCount++;
                    AddToSample(failureSample, message, reason!);
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

    /// <summary>
    /// A factual, non-fabricated description of what a job targeted, built from its own
    /// persisted filter fields — used both as the ledger operation's <c>ScopeDescription</c> and,
    /// for purge jobs, as its <c>Reason</c> (see <see cref="RecoveryContext"/> and the comment at
    /// its call site in <see cref="RunAsync"/>).
    /// </summary>
    private static string BuildFilterDescription(BulkOperationJob job)
    {
        var parts = new List<string> { $"bulk {job.OperationType.ToString().ToLowerInvariant()} job {job.Id}" };

        if (!string.IsNullOrEmpty(job.EntityNameFilter))
            parts.Add($"entity~={job.EntityNameFilter}");
        if (job.StatusFilter is { } status)
            parts.Add($"status={status}");
        if (job.CategoryFilter is { } category)
            parts.Add($"category={category}");
        if (job.FromFilter is { } from)
            parts.Add($"from={from:O}");
        if (job.ToFilter is { } to)
            parts.Add($"to={to:O}");

        return string.Join("; ", parts);
    }

    private async Task<(MessageOutcome Outcome, string? Reason)> ProcessMessageAsync(
        BulkOperationType operationType, DlqMessage message, RecoveryContext recovery, CancellationToken cancellationToken)
    {
        // A message already moved on (e.g. replayed manually between job creation and
        // execution) is skipped rather than re-attempted — the filter matched it at creation
        // time, but its current status may no longer reflect that.
        //
        // This guard applies to purge as well as replay. It was previously Replay-only, while
        // BulkOperationMatching.BuildQuery only constrains status when the caller supplied a
        // filter — so a purge job created without one matched Replayed, Discarded and Archived
        // rows too. Purging an already-Discarded message failed at the provider and inflated the
        // job's FailureCount; purging a successfully-Replayed one overwrote its status to
        // Discarded, corrupting the DLQ history and the replay audit narrative.
        if (message.Status != DlqMessageStatus.Active
            && message.Status != DlqMessageStatus.ReplayFailed)
        {
            var operationNoun = operationType == BulkOperationType.Replay ? "replay" : "purge";
            return (MessageOutcome.Skipped,
                $"Message status is now '{message.Status}', no longer eligible for {operationNoun}");
        }

        var (entityName, subscriptionName) = ResolveEntityAndSubscription(message);

        var actionKind = operationType == BulkOperationType.Replay ? RecoveryOperationKind.Replay : RecoveryOperationKind.Purge;
        var decision = await _eligibilityGate.EvaluateAsync(
            new RecoveryEligibilityRequest(
                recovery.OwnerId, actionKind, recovery.Actor.Kind, RecoveryTrigger.BulkJob,
                recovery.Namespace.Id, message.EntityName, message.BodyHash, SignatureHash: null,
                recovery.Namespace.Environment, Provider: recovery.Namespace.Provider),
            cancellationToken);

        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            await RecordDeclinedAsync(message, recovery, entityName, decision, cancellationToken);
            return (MessageOutcome.Skipped,
                $"Blocked by the Eligibility Gate ({decision.ReasonCode}) — escalate for manual review");
        }

        if (operationType == BulkOperationType.Replay)
        {
            // Claim the message via optimistic concurrency (Status is a concurrency token —
            // see DlqDbContext.ConfigureDlqMessage) before calling the live provider, so a
            // worker that loses the race never sends a duplicate — not just avoids a
            // duplicate DB row. A losing SaveChangesAsync throws DbUpdateConcurrencyException
            // here, before ReplayMessageAsync is ever invoked.
            message.Status = DlqMessageStatus.Replaying;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _dbContext.Entry(message).ReloadAsync(cancellationToken);
                return (MessageOutcome.Skipped, "Message was claimed by another concurrent replay — skipped");
            }

            // CancellationToken.None from here through the provider call: the claim above is
            // already committed, so this message must run to completion rather than be abandoned
            // mid-flight by a job cancellation — the same per-message persistence guarantee
            // RulesController.ReplayAll and AutoReplayExecutor already enforce.
            var beginResult = await _recoveryLedger.BeginEntryAsync(
                RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                    message, recovery.Namespace, recovery.OperationId, recovery.OwnerId, recovery.Actor, entityName),
                CancellationToken.None);

            if (beginResult.IsFailure)
            {
                // No message movement without ledger coverage: release the claim so a retry can
                // pick the message up again rather than call the provider unrecorded.
                message.Status = DlqMessageStatus.Active;
                return (MessageOutcome.Skipped, $"Recovery ledger error: {beginResult.Error.Message}");
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
            // safely retriable — see AwsMessageReceiver.ReplayMessageAsync.
            var executionOutcome = result.IsSuccess
                ? RecoveryExecutionOutcome.Accepted
                : result.Error.Code == "AWS.SQS.ReplayAmbiguous"
                    ? RecoveryExecutionOutcome.Unknown
                    : RecoveryExecutionOutcome.Rejected;

            // CancellationToken.None: the provider call above already happened, so this outcome
            // must be recorded even if cancellation was requested in the meantime — the same
            // reasoning SignatureReplayExecutor.ProcessMessageAsync's post-provider-call save
            // uses. A cancelled RecordExecutionAsync here would throw past this method's return,
            // silently dropping ProcessedCount/SuccessCount for a replay that already succeeded.
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
                ReplayedBy = "bulk-operation",
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
                return (MessageOutcome.Success, null);
            }

            message.Status = DlqMessageStatus.ReplayFailed;
            message.ReplaySuccess = false;
            return (MessageOutcome.Failure, result.Error.Message);
        }

        // Purge — claimed exactly the way replay is, for the same reason. Status is an EF
        // concurrency token, so committing the claim before the provider call means a worker
        // that loses the race never issues a second delete; its SaveChangesAsync throws here,
        // before PurgeMessageAsync is invoked. Previously the purge branch had no claim at all,
        // so two concurrent jobs both called the provider for the same message and the loser
        // recorded a spurious failure against a message that had been purged correctly.
        message.Status = DlqMessageStatus.Purging;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(message).ReloadAsync(cancellationToken);
            return (MessageOutcome.Skipped, "Message was claimed by another concurrent purge — skipped");
        }

        // CancellationToken.None from here through the provider call — see the replay branch
        // above for why an already-committed claim must run to completion.
        var purgeBeginResult = await _recoveryLedger.BeginEntryAsync(
            RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                message, recovery.Namespace, recovery.OperationId, recovery.OwnerId, recovery.Actor, entityName),
            CancellationToken.None);

        if (purgeBeginResult.IsFailure)
        {
            message.Status = DlqMessageStatus.Active;
            return (MessageOutcome.Skipped, $"Recovery ledger error: {purgeBeginResult.Error.Message}");
        }

        var purgeEntry = purgeBeginResult.Value;

        if (decision.ReasonCode is not null)
        {
            await RecordRecurrenceContextAsync(purgeEntry.Id, recovery, decision, CancellationToken.None);
        }

        var purgeResult = await _messageOperationsService.PurgeMessageAsync(
            message.NamespaceId, entityName, subscriptionName, message.SequenceNumber,
            fromDeadLetter: true, CancellationToken.None);

        // CancellationToken.None — same reasoning as the replay branch above: the provider call
        // already happened, so its outcome must be recorded regardless of cancellation requested
        // in the meantime.
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = purgeEntry.Id,
            OwnerId = recovery.OwnerId,
            Actor = recovery.Actor,
            Outcome = purgeResult.IsSuccess ? RecoveryExecutionOutcome.Accepted : RecoveryExecutionOutcome.Rejected,
            ProviderDetailJson = purgeResult.IsSuccess ? null : purgeResult.Error.Message,
        }, CancellationToken.None);

        if (purgeResult.IsSuccess)
        {
            message.Status = DlqMessageStatus.Discarded;
            return (MessageOutcome.Success, null);
        }

        // Release the claim so a retry can pick the message up again. Leaving it Purging would
        // reproduce the stranded-claim defect that InterruptedOperationRecovery exists to fix.
        message.Status = DlqMessageStatus.Active;
        return (MessageOutcome.Failure, purgeResult.Error.Message);
    }

    /// <summary>
    /// Records a truthful <c>Declined</c> ledger entry for an Eligibility Gate verdict that
    /// blocked this message (roadmap §9.3) — never called for a rate limit, since bulk jobs have
    /// no per-rule rate-limit concept, so every non-<c>Allow</c> verdict reaching this executor is
    /// a real safety escalation, not routine throttling. Best-effort: a ledger-write failure never
    /// changes the underlying skip decision (roadmap §18), matching Phase A's same guarantee.
    /// </summary>
    private async Task RecordDeclinedAsync(
        DlqMessage message, RecoveryContext recovery, string entityName, EligibilityDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recoveryLedger.RecordDeclinedAsync(
                RecoveryLedgerEntrySnapshot.BuildBeginEntryRequest(
                    message, recovery.Namespace, recovery.OperationId, recovery.OwnerId, recovery.Actor, entityName),
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

    /// <summary>
    /// Reconstructs the queue/subscription pair a live replay/purge call needs from the
    /// persisted <see cref="DlqMessage"/>'s combined <c>EntityName</c> + <c>TopicName</c> —
    /// mirroring the same convention <c>DlqMonitorService.ParseEntity</c> used to store it and
    /// <c>RulesController.ReplayAll</c> used to reconstruct it. Internal (not private) so
    /// <see cref="SignatureReplay.SignatureReplayExecutor"/> can reuse it instead of duplicating
    /// this parsing.
    /// </summary>
    internal static (string EntityName, string? SubscriptionName) ResolveEntityAndSubscription(DlqMessage message)
    {
        if (message.EntityType != ServiceBusEntityType.Subscription || message.TopicName is null)
            return (message.EntityName, null);

        var prefix = $"{message.TopicName}{SubscriptionPathSegment}";
        var subscriptionName = message.EntityName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? message.EntityName[prefix.Length..]
            : message.EntityName;

        return (message.TopicName, subscriptionName);
    }

    private static void AddToSample(List<BulkOperationFailureSample> sample, DlqMessage message, string reason)
    {
        if (sample.Count >= MaxFailureSampleSize)
            return;

        sample.Add(new BulkOperationFailureSample(message.MessageId, message.EntityName, reason));
    }

    private void RecordCompletionAudit(BulkOperationJob job)
    {
        // Runs on the background worker, outside the HTTP request pipeline — no HttpContext
        // exists here, so IAuditLogger (which requires one) can't be used. IAuditService.Enqueue
        // takes a plain AuditLog entity for exactly this kind of context-free background write.
        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = job.OwnerId,
            UserIdentity = "system:bulk-operation",
            Action = $"bulk:{job.OperationType.ToString().ToLowerInvariant()}",
            Outcome = job.Status.ToString(),
            NamespaceId = job.NamespaceId,
            NamespaceName = job.NamespaceDisplayName,
            ResourceName = job.EntityNameFilter,
            DetailsJson = JsonSerializer.Serialize(new
            {
                jobId = job.Id,
                totalMatched = job.TotalMatched,
                processed = job.ProcessedCount,
                succeeded = job.SuccessCount,
                failed = job.FailureCount,
                skipped = job.SkippedCount,
            }),
            ErrorDetails = job.ErrorSummary,
            CorrelationId = job.CorrelationId,
        });
    }

    /// <summary>
    /// Publishes <see cref="EventTypes.BulkOperationCompleted"/> so subscribers — currently
    /// <c>WebhookBulkOperationCompletedHandler</c>, bridging to Slack/Teams/generic webhook
    /// alerts — learn the job finished. <see cref="IPlatformEventBus.PublishAsync"/> is
    /// non-blocking by contract, so this never slows down the worker's next job.
    /// </summary>
    private async Task PublishCompletionEventAsync(BulkOperationJob job)
    {
        var payload = new BulkOperationCompletedPayload
        {
            JobId = job.Id,
            OperationType = job.OperationType,
            Status = job.Status,
            NamespaceId = job.NamespaceId,
            NamespaceName = job.NamespaceDisplayName,
            TotalMatched = job.TotalMatched,
            SuccessCount = job.SuccessCount,
            FailureCount = job.FailureCount,
            SkippedCount = job.SkippedCount,
            CompletedAtUtc = job.CompletedAt ?? DateTimeOffset.UtcNow,
        };

        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BulkOperations.BulkOperationExecutor",
            Category = EventCategories.BulkOperation,
            EventType = EventTypes.BulkOperationCompleted,
            Severity = job.Status is BulkOperationStatus.Failed or BulkOperationStatus.CompletedWithErrors
                ? EventSeverity.Warning
                : EventSeverity.Info,
            NamespaceId = job.NamespaceId,
            NamespaceName = job.NamespaceDisplayName,
            CorrelationId = job.CorrelationId,
            Actor = $"owner:{job.OwnerId}",
            Payload = payload,
        };

        await _eventBus.PublishAsync(evt, CancellationToken.None);
    }

    private enum MessageOutcome
    {
        Success,
        Failure,
        Skipped,
    }
}
