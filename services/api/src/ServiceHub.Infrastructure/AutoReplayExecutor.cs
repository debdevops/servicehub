using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Executes the replay action for auto-replay rules.
/// Handles rate limiting, Service Bus interaction, replay-history persistence,
/// and DLQ message status updates.
/// </summary>
public sealed class AutoReplayExecutor : IAutoReplayExecutor
{
    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<AutoReplayExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoReplayExecutor"/> class.
    /// </summary>
    public AutoReplayExecutor(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector protector,
        ILogger<AutoReplayExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<string>> ExecuteAsync(
        DlqMessage message,
        AutoReplayRule rule,
        RuleAction action,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing auto-replay rule {RuleId}/{RuleName} on message {MessageId} (DLQ record {DlqId})",
            rule.Id, rule.Name, message.MessageId, message.Id);

        // Rate-limit check
        if (!await CanReplayAsync(rule.Id, cancellationToken))
        {
            _logger.LogWarning(
                "Rule {RuleId} exceeded rate limit ({Max}/hour), skipping",
                rule.Id, rule.MaxReplaysPerHour);
            return Result<string>.Failure(
                Error.Validation("Rule.RateLimited", $"Rule '{rule.Name}' has exceeded {rule.MaxReplaysPerHour} replays/hour"));
        }

        // Resolve the namespace connection
        var nsResult = await _namespaceRepository.GetByIdAsync(message.NamespaceId);
        if (nsResult.IsFailure)
            return Result<string>.Failure(nsResult.Error);

        var ns = nsResult.Value;
        if (string.IsNullOrWhiteSpace(ns.ConnectionString))
            return Result<string>.Failure(
                Error.Validation("Namespace.ConnectionString", "Namespace has no connection string"));

        var unprotectResult = _protector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
            return Result<string>.Failure(unprotectResult.Error);

        var client = _clientCache.GetOrCreate(message.NamespaceId, unprotectResult.Value);

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

        // Execute the replay
        try
        {
            var replayResult = await client.ReplayMessageAsync(
                entityName, subscriptionName, message.SequenceNumber, cancellationToken);

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
                message.MessageId, outcome);

            return replayResult.IsSuccess
                ? Result<string>.Success(outcome)
                : Result<string>.Failure(replayResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-replay failed for message {MessageId}", message.MessageId);

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
