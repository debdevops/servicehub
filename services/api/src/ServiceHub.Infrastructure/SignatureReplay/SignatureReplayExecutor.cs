using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// <inheritdoc cref="ISignatureReplayExecutor"/>
/// </summary>
/// <remarks>
/// Every message is processed through <see cref="IMessageOperationsService"/> — the same call
/// <see cref="BulkOperations.BulkOperationExecutor"/> and single-message replay already use — so
/// this class contributes no new replay behavior, only the loop, progress tracking, and
/// cancellation around it.
/// </remarks>
public sealed class SignatureReplayExecutor : ISignatureReplayExecutor
{
    private const int MaxFailureSampleSize = 20;
    private const int SaveProgressEveryNMessages = 5;

    private readonly DlqDbContext _dbContext;
    private readonly IMessageOperationsService _messageOperationsService;
    private readonly ILogger<SignatureReplayExecutor> _logger;

    /// <summary>Initialises a new instance of <see cref="SignatureReplayExecutor"/>.</summary>
    public SignatureReplayExecutor(
        DlqDbContext dbContext,
        IMessageOperationsService messageOperationsService,
        ILogger<SignatureReplayExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(SignatureReplayJobState job, CancellationToken cancellationToken)
    {
        lock (job.SyncRoot)
        {
            if (job.CancellationRequestedAt.HasValue)
            {
                job.Status = BulkOperationStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }

            job.Status = BulkOperationStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (job.SyncRoot)
            {
                job.Status = BulkOperationStatus.Cancelled;
            }

            _logger.LogInformation(
                "Signature replay job {JobId} cancelled after processing {Processed}/{Total} message(s)",
                job.Id, job.ProcessedCount, job.TotalMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature replay job {JobId} failed unexpectedly", job.Id);
            lock (job.SyncRoot)
            {
                job.Status = BulkOperationStatus.Failed;
                job.ErrorSummary = ex.Message;
            }
        }
        finally
        {
            lock (job.SyncRoot)
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
                if (job.Status == BulkOperationStatus.Running)
                {
                    job.Status = job.FailureCount > 0 || job.SkippedCount > 0
                        ? BulkOperationStatus.CompletedWithErrors
                        : BulkOperationStatus.Completed;
                }
            }
        }
    }

    private async Task RunAsync(SignatureReplayJobState job, CancellationToken cancellationToken)
    {
        var messages = await _dbContext.DlqMessages
            .Where(m => job.MessageIds.Contains(m.Id))
            .OrderBy(m => m.DetectedAtUtc)
            .ToListAsync(cancellationToken);

        var sinceLastSave = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (outcome, reason) = await ProcessMessageAsync(message, cancellationToken);

            lock (job.SyncRoot)
            {
                job.ProcessedCount++;
                switch (outcome)
                {
                    case MessageOutcome.Success:
                        job.SuccessCount++;
                        break;
                    case MessageOutcome.Failure:
                        job.FailureCount++;
                        AddToSample(job.FailureSample, message, reason!);
                        break;
                    case MessageOutcome.Skipped:
                        job.SkippedCount++;
                        AddToSample(job.FailureSample, message, reason!);
                        break;
                }
            }

            if (++sinceLastSave >= SaveProgressEveryNMessages)
            {
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                sinceLastSave = 0;
            }
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(MessageOutcome Outcome, string? Reason)> ProcessMessageAsync(
        DlqMessage message, CancellationToken cancellationToken)
    {
        // A message already moved on (e.g. replayed manually between job creation and
        // execution) is skipped rather than re-attempted — the filter matched it at creation
        // time, but its current status may no longer reflect that. Same eligibility rule
        // BulkOperationExecutor.ProcessMessageAsync applies.
        if (message.Status != DlqMessageStatus.Active && message.Status != DlqMessageStatus.ReplayFailed)
        {
            return (MessageOutcome.Skipped, $"Message status is now '{message.Status}', no longer eligible for replay");
        }

        var (entityName, subscriptionName) = BulkOperationExecutor.ResolveEntityAndSubscription(message);

        var result = await _messageOperationsService.ReplayMessageAsync(
            message.NamespaceId, entityName, subscriptionName, message.SequenceNumber, cancellationToken);

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
            return (MessageOutcome.Success, null);
        }

        message.Status = DlqMessageStatus.ReplayFailed;
        message.ReplaySuccess = false;
        return (MessageOutcome.Failure, result.Error.Message);
    }

    private static void AddToSample(List<BulkOperationFailureSample> sample, DlqMessage message, string reason)
    {
        if (sample.Count >= MaxFailureSampleSize)
            return;

        sample.Add(new BulkOperationFailureSample(message.MessageId, message.EntityName, reason));
    }

    private enum MessageOutcome
    {
        Success,
        Failure,
        Skipped,
    }
}
