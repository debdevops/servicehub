using Microsoft.EntityFrameworkCore;
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
/// Handles rate limiting, provider-routed replay via <see cref="IMessageOperationsService"/>,
/// replay-history persistence, and DLQ message status updates.
/// </summary>
public sealed class AutoReplayExecutor : IAutoReplayExecutor
{
    private readonly DlqDbContext _dbContext;
    private readonly IMessageOperationsService _messageOperations;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly ILogger<AutoReplayExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoReplayExecutor"/> class.
    /// </summary>
    public AutoReplayExecutor(
        DlqDbContext dbContext,
        IMessageOperationsService messageOperations,
        IRecoveryLedger recoveryLedger,
        ILogger<AutoReplayExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _messageOperations = messageOperations ?? throw new ArgumentNullException(nameof(messageOperations));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Rate-limit check
        if (!await CanReplayAsync(rule.Id, cancellationToken))
        {
            _logger.LogWarning(
                "Rule {RuleId} exceeded rate limit ({Max}/hour), skipping",
                rule.Id, rule.MaxReplaysPerHour);
            return Result<string>.Failure(
                Error.Validation("Rule.RateLimited", $"Rule '{rule.Name}' has exceeded {rule.MaxReplaysPerHour} replays/hour"));
        }

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
                message, ns, operationId, rule.OwnerId, actor, entityName),
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

            // CancellationToken.None: the provider call above already happened, so this outcome
            // must be recorded even if cancellation was requested in the meantime.
            await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = rule.OwnerId,
                Actor = actor,
                Outcome = replayResult.IsSuccess ? RecoveryExecutionOutcome.Accepted : RecoveryExecutionOutcome.Rejected,
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

            await _dbContext.SaveChangesAsync(cancellationToken);

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

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<string>.Failure(Error.Internal("AutoReplay.Exception", ex.Message));
        }
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
}
