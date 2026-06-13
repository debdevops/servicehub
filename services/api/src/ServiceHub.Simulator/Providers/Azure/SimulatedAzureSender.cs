using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Azure;

/// <summary>
/// Simulated Azure Service Bus message sender backed by <see cref="ISimulatorStore"/>.
/// All operations are in-memory and credential-free.
/// </summary>
public sealed class SimulatedAzureSender : IMessageSender
{
    private readonly ISimulatorStore _store;

    /// <summary>Initializes a new instance of <see cref="SimulatedAzureSender"/>.</summary>
    public SimulatedAzureSender(ISimulatorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.NamespaceId is null)
            return Task.FromResult(Result.Failure(Error.Validation("Simulator.MissingNamespace", "NamespaceId is required")));
        if (string.IsNullOrWhiteSpace(request.EntityName))
            return Task.FromResult(Result.Failure(Error.Validation("Simulator.MissingEntity", "EntityName is required")));

        var entity = _store.GetEntity(request.NamespaceId.Value, request.EntityName);
        if (entity is null)
            return Task.FromResult(Result.Failure(
                Error.NotFound("Simulator.EntityNotFound", $"Entity '{request.EntityName}' not found in namespace {request.NamespaceId}")));

        var seq = entity.NextSequenceNumber();
        var msg = new SimulatorMessage(
            MessageId: Guid.NewGuid().ToString(),
            SequenceNumber: seq,
            Body: request.Body ?? string.Empty,
            ContentType: request.ContentType,
            CorrelationId: request.CorrelationId,
            SessionId: request.SessionId,
            PartitionKey: request.PartitionKey,
            Subject: request.Subject,
            DeliveryCount: 0,
            EnqueuedTime: DateTimeOffset.UtcNow,
            ScheduledEnqueueTime: request.ScheduledEnqueueTimeUtc,
            IsDeadLettered: false,
            DeadLetterReason: null,
            DeadLetterErrorDescription: null,
            ApplicationProperties: request.ApplicationProperties
                ?? new Dictionary<string, object>(),
            SizeInBytes: System.Text.Encoding.UTF8.GetByteCount(request.Body ?? string.Empty),
            ReceiptHandle: null,
            VisibilityUntil: null,
            OrderingKey: null,
            DeliveryAttempt: 0,
            AckDeadline: null,
            IsNacked: false,
            Provider: Core.Enums.CloudProviderType.Azure);

        entity.EnqueueMessage(msg);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public async Task<Result> SendBatchAsync(
        IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        foreach (var request in requests)
        {
            var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
                return result;
        }

        return Result.Success();
    }
}
