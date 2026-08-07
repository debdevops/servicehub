using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

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
    private const int SaveProgressEveryNMessages = 5;
    private const int MaxFailureSampleSize = 20;
    private const string SubscriptionPathSegment = "/subscriptions/";

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IMessageOperationsService _messageOperationsService;
    private readonly IAuditService _auditService;
    private readonly IPlatformEventBus _eventBus;
    private readonly ILogger<BulkOperationExecutor> _logger;

    /// <summary>Initialises a new instance of <see cref="BulkOperationExecutor"/>.</summary>
    public BulkOperationExecutor(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IMessageOperationsService messageOperationsService,
        IAuditService auditService,
        IPlatformEventBus eventBus,
        ILogger<BulkOperationExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

            await _dbContext.SaveChangesAsync(CancellationToken.None);
            RecordCompletionAudit(job);
            await PublishCompletionEventAsync(job);
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

        var failureSample = new List<BulkOperationFailureSample>();
        var sinceLastSave = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (outcome, reason) = await ProcessMessageAsync(job.OperationType, message, cancellationToken);
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
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                sinceLastSave = 0;
            }
        }

        job.FailureSampleJson = failureSample.Count > 0 ? JsonSerializer.Serialize(failureSample) : null;
    }

    private async Task<(MessageOutcome Outcome, string? Reason)> ProcessMessageAsync(
        BulkOperationType operationType, DlqMessage message, CancellationToken cancellationToken)
    {
        // A message already moved on (e.g. replayed manually between job creation and
        // execution) is skipped rather than re-attempted — the filter matched it at creation
        // time, but its current status may no longer reflect that.
        if (operationType == BulkOperationType.Replay && message.Status != DlqMessageStatus.Active
            && message.Status != DlqMessageStatus.ReplayFailed)
        {
            return (MessageOutcome.Skipped, $"Message status is now '{message.Status}', no longer eligible for replay");
        }

        var (entityName, subscriptionName) = ResolveEntityAndSubscription(message);

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

            var result = await _messageOperationsService.ReplayMessageAsync(
                message.NamespaceId, entityName, subscriptionName, message.SequenceNumber, cancellationToken);

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

        // Purge
        var purgeResult = await _messageOperationsService.PurgeMessageAsync(
            message.NamespaceId, entityName, subscriptionName, message.SequenceNumber,
            fromDeadLetter: true, cancellationToken);

        if (purgeResult.IsSuccess)
        {
            message.Status = DlqMessageStatus.Discarded;
            return (MessageOutcome.Success, null);
        }

        return (MessageOutcome.Failure, purgeResult.Error.Message);
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
