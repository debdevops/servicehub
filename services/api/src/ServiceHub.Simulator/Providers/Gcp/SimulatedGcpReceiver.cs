using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Gcp;

/// <summary>
/// Simulated GCP Pub/Sub message receiver backed by <see cref="ISimulatorStore"/>.
/// Extends <see cref="IMessageReceiver"/> with <see cref="IAckDeadlineStatusProvider"/>
/// so the <c>CloudBridgeController</c> can retrieve ack-deadline status without
/// requiring real GCP credentials.
/// </summary>
public sealed class SimulatedGcpReceiver : IMessageReceiver, IAckDeadlineStatusProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatorClock _clock;

    /// <summary>Initializes a new instance of <see cref="SimulatedGcpReceiver"/>.</summary>
    public SimulatedGcpReceiver(ISimulatorStore store, SimulatorClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasFault(request.NamespaceId, request.EntityName, "AckDeadlineStorm"))
        {
            // Under ack-deadline storm all messages get a 1-second deadline
            var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
            if (entity is not null)
                foreach (var msg in entity.PeekMessages(int.MaxValue))
                    entity.SetAckDeadline(msg.MessageId, 1);
        }

        if (HasFault(request.NamespaceId, request.EntityName, "NetworkTimeout"))
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.ExternalService("SimFault.NetworkTimeout", "Simulated network timeout")));

        var e = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (e is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound",
                    $"Subscription '{request.EntityName}' not found in namespace {request.NamespaceId}")));

        var rawMessages = e.PeekMessages(request.MaxMessages);
        foreach (var msg in rawMessages)
            e.SetAckDeadline(msg.MessageId, e.AckDeadlineSeconds);

        var messages = rawMessages
            .Select(m => MapToMessage(m, request.NamespaceId, request.EntityName, request.SubscriptionName))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(messages));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound",
                    $"Subscription '{request.EntityName}' not found")));

        var messages = entity.PeekDlq(request.MaxMessages)
            .Select(m => MapToMessage(m, request.NamespaceId, request.EntityName, request.SubscriptionName))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(messages));
    }

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId, string entityName, string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result<long>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{entityName}' not found")));

        return Task.FromResult(Result<long>.Success(entity.GetMessageCount() + entity.GetDlqCount()));
    }

    /// <inheritdoc/>
    public Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result<int>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{request.EntityName}' not found")));

        var messages = entity.PeekMessages(request.MessageCount);
        var moved = 0;
        foreach (var msg in messages)
        {
            entity.MoveToDeadLetter(msg.SequenceNumber, request.Reason, request.ErrorDescription ?? string.Empty);
            moved++;
        }

        return Task.FromResult(Result<int>.Success(moved));
    }

    /// <inheritdoc/>
    public Task<Result> ReplayMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{entityName}' not found")));

        return Task.FromResult(entity.ReplayFromDlq(sequenceNumber)
            ? Result.Success()
            : Result.Failure(Error.NotFound("Simulator.MessageNotFound",
                $"Message {sequenceNumber} not found in DLQ")));
    }

    /// <inheritdoc/>
    public Task<Result> PurgeMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{entityName}' not found")));

        return Task.FromResult(entity.Purge(sequenceNumber, fromDeadLetter)
            ? Result.Success()
            : Result.Failure(Error.NotFound("Simulator.MessageNotFound",
                $"Message {sequenceNumber} not found")));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        int maxMessages, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Subscription '{entityName}' not found")));

        var now = _clock.UtcNow;
        var scheduled = entity.PeekMessages(int.MaxValue)
            .Where(m => m.ScheduledEnqueueTime.HasValue && m.ScheduledEnqueueTime.Value > now)
            .Take(maxMessages)
            .Select(m => MapToMessage(m, namespaceId, entityName, subscriptionName))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(scheduled));
    }

    // ── IAckDeadlineStatusProvider ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<GcpAckDeadlineStatus>> GetAckDeadlineStatusAsync(
        Guid namespaceId, string subscriptionId, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, subscriptionId);
        if (entity is null)
            return Task.FromResult(Result<GcpAckDeadlineStatus>.Failure(
                Error.NotFound("Simulator.EntityNotFound",
                    $"Subscription '{subscriptionId}' not found")));

        var status = new GcpAckDeadlineStatus(
            entity.AckDeadlineSeconds,
            entity.DeadLetterTopicName is not null,
            entity.DeadLetterTopicName,
            entity.MaxDeliveryAttempts > 0 ? entity.MaxDeliveryAttempts : null,
            entity.MessageOrderingEnabled);

        return Task.FromResult(Result<GcpAckDeadlineStatus>.Success(status));
    }

    // ── Fault helpers ─────────────────────────────────────────────────────────

    private bool HasFault(Guid namespaceId, string entityName, string faultType) =>
        _store.GetActiveFaultsFor(namespaceId, entityName).Any(f => f.FaultType == faultType);

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static Message MapToMessage(
        SimulatorMessage m, Guid namespaceId, string entityName, string? subscriptionName) =>
        new()
        {
            MessageId = m.MessageId,
            SequenceNumber = m.SequenceNumber,
            Body = m.Body,
            ContentType = m.ContentType,
            CorrelationId = m.CorrelationId,
            SessionId = m.SessionId,
            PartitionKey = m.PartitionKey,
            Subject = m.Subject,
            DeliveryCount = m.DeliveryCount,
            EnqueuedTime = m.EnqueuedTime,
            ScheduledEnqueueTime = m.ScheduledEnqueueTime,
            IsFromDeadLetter = m.IsDeadLettered,
            DeadLetterReason = m.DeadLetterReason,
            DeadLetterErrorDescription = m.DeadLetterErrorDescription,
            ApplicationProperties = m.ApplicationProperties,
            SizeInBytes = m.SizeInBytes,
            State = m.ScheduledEnqueueTime.HasValue && m.ScheduledEnqueueTime.Value > DateTimeOffset.UtcNow
                ? Core.Enums.MessageState.Scheduled
                : m.IsDeadLettered ? Core.Enums.MessageState.DeadLettered : Core.Enums.MessageState.Active,
            NamespaceId = namespaceId,
            EntityName = entityName,
            SubscriptionName = subscriptionName,
            LockedUntil = m.AckDeadline,
            LockToken = m.MessageId,
        };
}
