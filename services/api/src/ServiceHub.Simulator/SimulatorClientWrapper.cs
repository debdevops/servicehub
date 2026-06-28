using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// Simulated implementation of <see cref="IServiceBusClientWrapper"/> that routes operations
/// to the in-memory simulator store and receivers/senders.
/// </summary>
public sealed class SimulatorClientWrapper : IServiceBusClientWrapper
{
    private readonly ISimulatorStore _store;
    private readonly IMessageReceiver _messageReceiver;
    private readonly IMessageSender _messageSender;

    /// <inheritdoc/>
    public Guid NamespaceId { get; }

    /// <inheritdoc/>
    public bool IsClosed => false;

    /// <summary>
    /// Initializes a new instance of <see cref="SimulatorClientWrapper"/>.
    /// </summary>
    public SimulatorClientWrapper(
        Guid namespaceId,
        ISimulatorStore store,
        IMessageReceiver messageReceiver,
        IMessageSender messageSender)
    {
        NamespaceId = namespaceId;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
    }

    /// <inheritdoc/>
    public Task<Result> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Ensure request has the current NamespaceId set
        var req = request with { NamespaceId = NamespaceId };
        return _messageSender.SendAsync(req, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var req = request with { NamespaceId = NamespaceId };
        if (req.FromDeadLetter)
        {
            return _messageReceiver.PeekDeadLetterMessagesAsync(req, cancellationToken);
        }
        return _messageReceiver.PeekMessagesAsync(req, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<bool>.Success(true));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<QueueRuntimePropertiesDto>>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var entities = _store.GetEntities(NamespaceId);
        var ns = _store.GetNamespace(NamespaceId);

        List<QueueRuntimePropertiesDto> queues;
        if (ns?.Provider == CloudProviderType.Gcp)
        {
            // For GCP, we represent Pub/Sub subscriptions as queues in the UI
            queues = entities
                .Where(e => e.EntityType == "Subscription")
                .Select(MapToQueueDto)
                .ToList();
        }
        else
        {
            queues = entities
                .Where(e => e.EntityType == "Queue")
                .Select(MapToQueueDto)
                .ToList();
        }

        return Task.FromResult(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(queues));
    }

    /// <inheritdoc/>
    public Task<Result<QueueRuntimePropertiesDto>> GetQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(NamespaceId, queueName);
        if (entity is null)
        {
            return Task.FromResult(Result.Failure<QueueRuntimePropertiesDto>(
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{queueName}' not found.")));
        }
        return Task.FromResult(Result.Success(MapToQueueDto(entity)));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<TopicRuntimePropertiesDto>>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        var entities = _store.GetEntities(NamespaceId)
            .Where(e => e.EntityType == "Topic")
            .Select(MapToTopicDto)
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(entities));
    }

    /// <inheritdoc/>
    public Task<Result<TopicRuntimePropertiesDto>> GetTopicAsync(string topicName, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(NamespaceId, topicName);
        if (entity is null || entity.EntityType != "Topic")
        {
            return Task.FromResult(Result.Failure<TopicRuntimePropertiesDto>(
                Error.NotFound("Simulator.EntityNotFound", $"Topic '{topicName}' not found.")));
        }
        return Task.FromResult(Result.Success(MapToTopicDto(entity)));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetSubscriptionsAsync(string topicName, CancellationToken cancellationToken = default)
    {
        var subs = _store.GetEntities(NamespaceId)
            .Where(e => e.EntityType == "Subscription" && e.Name.StartsWith(topicName + "/subscriptions/", StringComparison.Ordinal))
            .Select(e => MapToSubscriptionDto(e, topicName))
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(subs));
    }

    /// <inheritdoc/>
    public Task<Result<SubscriptionRuntimePropertiesDto>> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken = default)
    {
        var fullName = $"{topicName}/subscriptions/{subscriptionName}";
        var entity = _store.GetEntity(NamespaceId, fullName);
        if (entity is null || entity.EntityType != "Subscription")
        {
            return Task.FromResult(Result.Failure<SubscriptionRuntimePropertiesDto>(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{subscriptionName}' not found on topic '{topicName}'.")));
        }
        return Task.FromResult(Result.Success(MapToSubscriptionDto(entity, topicName)));
    }

    /// <inheritdoc/>
    public async Task<int> DeadLetterMessagesAsync(
        string entityName,
        string? subscriptionName,
        int messageCount,
        string reason,
        string? errorDescription,
        CancellationToken cancellationToken = default)
    {
        var req = new DeadLetterRequest(
            NamespaceId: NamespaceId,
            EntityName: entityName,
            SubscriptionName: subscriptionName,
            MessageCount: messageCount,
            Reason: reason,
            ErrorDescription: errorDescription);

        var result = await _messageReceiver.DeadLetterMessagesAsync(req, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : 0;
    }

    /// <inheritdoc/>
    public Task<Result> ReplayMessageAsync(
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        return _messageReceiver.ReplayMessageAsync(NamespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<long, Result>> ReplayMessagesAsync(
        string entityName,
        string? subscriptionName,
        IReadOnlyCollection<long> sequenceNumbers,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, Result>();
        foreach (var seq in sequenceNumbers)
        {
            var res = await ReplayMessageAsync(entityName, subscriptionName, seq, cancellationToken).ConfigureAwait(false);
            results[seq] = res;
        }
        return results;
    }

    /// <inheritdoc/>
    public Task<Result> PurgeMessageAsync(
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        bool fromDeadLetter,
        CancellationToken cancellationToken = default)
    {
        return _messageReceiver.PurgeMessageAsync(NamespaceId, entityName, subscriptionName, sequenceNumber, fromDeadLetter, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        string entityName,
        string? subscriptionName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        return _messageReceiver.GetScheduledMessagesAsync(NamespaceId, entityName, subscriptionName, maxMessages, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result> CancelScheduledMessageAsync(
        string entityName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(NamespaceId, entityName);
        if (entity is null)
        {
            return Task.FromResult(Result.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{entityName}' not found.")));
        }
        var found = entity.Purge(sequenceNumber, fromDlq: false);
        return Task.FromResult(found
            ? Result.Success()
            : Result.Failure(Error.NotFound("Simulator.MessageNotFound", $"Scheduled message with sequence number {sequenceNumber} not found.")));
    }

    /// <inheritdoc/>
    public Task<Result<long>> ScheduleMessageAsync(
        SendMessageRequest request,
        DateTimeOffset scheduledTimeUtc,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return Task.FromResult(Result.Failure<long>(
                Error.Validation("Simulator.NullRequest", "Request is required.")));
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return Task.FromResult(Result.Failure<long>(
                Error.Validation("Simulator.MissingEntity", "Entity name is required.")));
        }

        var entity = _store.GetEntity(NamespaceId, request.EntityName);
        if (entity is null)
        {
            return Task.FromResult(Result.Failure<long>(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{request.EntityName}' not found.")));
        }

        var sequenceNumber = entity.NextSequenceNumber();
        var messageId = Guid.NewGuid().ToString();
        var msg = new SimulatorMessage(
            MessageId: messageId,
            SequenceNumber: sequenceNumber,
            Body: request.Body ?? string.Empty,
            ContentType: request.ContentType,
            CorrelationId: request.CorrelationId,
            SessionId: request.SessionId,
            PartitionKey: request.PartitionKey,
            Subject: request.Subject,
            DeliveryCount: 0,
            EnqueuedTime: DateTimeOffset.UtcNow,
            ScheduledEnqueueTime: scheduledTimeUtc,
            IsDeadLettered: false,
            DeadLetterReason: null,
            DeadLetterErrorDescription: null,
            ApplicationProperties: request.ApplicationProperties ?? new Dictionary<string, object>(),
            SizeInBytes: System.Text.Encoding.UTF8.GetByteCount(request.Body ?? string.Empty),
            ReceiptHandle: null,
            VisibilityUntil: null,
            OrderingKey: null,
            DeliveryAttempt: 0,
            AckDeadline: null,
            IsNacked: false,
            Provider: entity.Provider
        );

        entity.EnqueueMessage(msg);
        return Task.FromResult(Result<long>.Success(sequenceNumber));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // ── Mapping Helpers ───────────────────────────────────────────────────────

    private QueueRuntimePropertiesDto MapToQueueDto(SimulatorEntity entity)
    {
        var now = DateTimeOffset.UtcNow;
        var activeMessages = entity.PeekMessages(int.MaxValue);
        var scheduledCount = activeMessages.Count(m => m.ScheduledEnqueueTime.HasValue && m.ScheduledEnqueueTime.Value > now);

        return new QueueRuntimePropertiesDto(
            Name: entity.Name,
            ActiveMessageCount: entity.GetMessageCount(),
            DeadLetterMessageCount: entity.GetDlqCount(),
            ScheduledMessageCount: scheduledCount,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            SizeInBytes: 1024,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024,
            MaxDeliveryCount: entity.MaxDeliveryAttempts,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue
        );
    }

    private TopicRuntimePropertiesDto MapToTopicDto(SimulatorEntity entity)
    {
        var subCount = _store.GetEntities(NamespaceId)
            .Count(e => e.EntityType == "Subscription" && e.Name.StartsWith(entity.Name + "/subscriptions/", StringComparison.Ordinal));

        return new TopicRuntimePropertiesDto(
            Name: entity.Name,
            SubscriptionCount: subCount,
            SizeInBytes: 1024,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            SupportOrdering: false,
            MaxSizeInMegabytes: 1024,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10)
        );
    }

    private SubscriptionRuntimePropertiesDto MapToSubscriptionDto(SimulatorEntity entity, string topicName)
    {
        var prefix = $"{topicName}/subscriptions/";
        var shortName = entity.Name.StartsWith(prefix, StringComparison.Ordinal)
            ? entity.Name[prefix.Length..]
            : entity.Name;

        return new SubscriptionRuntimePropertiesDto(
            Name: shortName,
            TopicName: topicName,
            ActiveMessageCount: entity.GetMessageCount(),
            DeadLetterMessageCount: entity.GetDlqCount(),
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: true,
            EnableDeadLetteringOnFilterEvaluationExceptions: true,
            MaxDeliveryCount: entity.MaxDeliveryAttempts,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: null,
            ForwardDeadLetteredMessagesTo: null
        );
    }
}
