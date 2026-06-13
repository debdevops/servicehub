using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Azure;

/// <summary>
/// Simulated Azure Service Bus message receiver backed by <see cref="ISimulatorStore"/>.
/// All operations are in-memory and credential-free.
/// </summary>
public sealed class SimulatedAzureReceiver : IMessageReceiver
{
    private readonly ISimulatorStore _store;
    private readonly SimulatorClock _clock;

    /// <summary>Initializes a new instance of <see cref="SimulatedAzureReceiver"/>.</summary>
    public SimulatedAzureReceiver(ISimulatorStore store, SimulatorClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasFault(request.NamespaceId, request.EntityName, "NetworkTimeout"))
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.ExternalService("SimFault.NetworkTimeout", "Simulated network timeout")));

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{request.EntityName}' not found in namespace {request.NamespaceId}")));

        var messages = entity.PeekMessages(request.MaxMessages)
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
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{request.EntityName}' not found in namespace {request.NamespaceId}")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{entityName}' not found")));

        var total = entity.GetMessageCount() + entity.GetDlqCount();
        return Task.FromResult(Result<long>.Success(total));
    }

    /// <inheritdoc/>
    public Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result<int>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{request.EntityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{entityName}' not found")));

        var replayed = entity.ReplayFromDlq(sequenceNumber);
        return Task.FromResult(replayed
            ? Result.Success()
            : Result.Failure(Error.NotFound("Simulator.MessageNotFound", $"Message {sequenceNumber} not found in DLQ")));
    }

    /// <inheritdoc/>
    public Task<Result> PurgeMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{entityName}' not found")));

        var purged = entity.Purge(sequenceNumber, fromDeadLetter);
        return Task.FromResult(purged
            ? Result.Success()
            : Result.Failure(Error.NotFound("Simulator.MessageNotFound", $"Message {sequenceNumber} not found")));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        int maxMessages, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, entityName);
        if (entity is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{entityName}' not found")));

        var now = _clock.UtcNow;
        var scheduled = entity.PeekMessages(int.MaxValue)
            .Where(m => m.ScheduledEnqueueTime.HasValue && m.ScheduledEnqueueTime.Value > now)
            .Take(maxMessages)
            .Select(m => MapToMessage(m, namespaceId, entityName, subscriptionName))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(scheduled));
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
                ? MessageState.Scheduled
                : m.IsDeadLettered ? MessageState.DeadLettered : MessageState.Active,
            NamespaceId = namespaceId,
            EntityName = entityName,
            SubscriptionName = subscriptionName,
            LockedUntil = null,
            LockToken = null,
        };
}
