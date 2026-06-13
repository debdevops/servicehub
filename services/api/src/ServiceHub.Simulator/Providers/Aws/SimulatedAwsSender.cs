using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Aws;

/// <summary>
/// Simulated AWS SQS message sender backed by <see cref="ISimulatorStore"/>.
/// </summary>
public sealed class SimulatedAwsSender : IMessageSender
{
    private readonly ISimulatorStore _store;

    /// <summary>Initializes a new instance of <see cref="SimulatedAwsSender"/>.</summary>
    public SimulatedAwsSender(ISimulatorStore store)
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
                Error.NotFound("Simulator.EntityNotFound",
                    $"Queue '{request.EntityName}' not found in namespace {request.NamespaceId}")));

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
            ApplicationProperties: request.ApplicationProperties ?? new Dictionary<string, object>(),
            SizeInBytes: System.Text.Encoding.UTF8.GetByteCount(request.Body ?? string.Empty),
            ReceiptHandle: Guid.NewGuid().ToString("N"),
            VisibilityUntil: null,
            OrderingKey: null,
            DeliveryAttempt: 0,
            AckDeadline: null,
            IsNacked: false,
            Provider: Core.Enums.CloudProviderType.Aws);

        entity.EnqueueMessage(msg);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public async Task<Result> SendBatchAsync(
        IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        foreach (var req in requests)
        {
            var result = await SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) return result;
        }

        return Result.Success();
    }
}
