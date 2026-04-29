using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Aws;

/// <summary>
/// Simulated AWS SQS message receiver backed by <see cref="ISimulatorStore"/>.
/// Extends <see cref="IMessageReceiver"/> with <see cref="IVisibilityStatusProvider"/>
/// so the <c>CloudBridgeController</c> can retrieve visibility-window status without
/// requiring real AWS credentials.
/// </summary>
public sealed class SimulatedAwsReceiver : IMessageReceiver, IVisibilityStatusProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatorClock _clock;

    /// <summary>Initializes a new instance of <see cref="SimulatedAwsReceiver"/>.</summary>
    public SimulatedAwsReceiver(ISimulatorStore store, SimulatorClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasFault(request.NamespaceId, request.EntityName, "KmsError"))
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.ExternalService("SimFault.KmsError",
                    "Simulated KMS key not accessible — check IAM permissions")));

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result<IReadOnlyList<Message>>.Failure(
                Error.NotFound("Simulator.EntityNotFound",
                    $"Queue '{request.EntityName}' not found in namespace {request.NamespaceId}")));

        // SQS peek sets a visibility window on returned messages
        var rawMessages = entity.PeekMessages(request.MaxMessages);
        foreach (var msg in rawMessages)
            entity.SetVisibilityWindow(msg.SequenceNumber, entity.VisibilityTimeoutSeconds);

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
                    $"Queue '{request.EntityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{entityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{request.EntityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{entityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{entityName}' not found")));

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
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{entityName}' not found")));

        var now = _clock.UtcNow;
        var scheduled = entity.PeekMessages(int.MaxValue)
            .Where(m => m.ScheduledEnqueueTime.HasValue && m.ScheduledEnqueueTime.Value > now)
            .Take(maxMessages)
            .Select(m => MapToMessage(m, namespaceId, entityName, subscriptionName))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(scheduled));
    }

    // ── IVisibilityStatusProvider ─────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<SqsVisibilityInfo>> GetVisibilityWindowStatusAsync(
        Guid namespaceId, string queueName, CancellationToken cancellationToken = default)
    {
        var entity = _store.GetEntity(namespaceId, queueName);
        if (entity is null)
            return Task.FromResult(Result<SqsVisibilityInfo>.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Queue '{queueName}' not found")));

        var info = new SqsVisibilityInfo(
            entity.GetInFlightMessages().Count,
            entity.VisibilityTimeoutSeconds,
            (int)entity.GetDlqCount());

        return Task.FromResult(Result<SqsVisibilityInfo>.Success(info));
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
            LockedUntil = m.VisibilityUntil,
            LockToken = m.ReceiptHandle,
        };
}
