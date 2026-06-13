using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Aws;

/// <summary>
/// Simulated AWS SQS/SNS messaging provider.
/// Validates connections instantly and lists entities from the in-memory store.
/// </summary>
public sealed class SimulatedAwsMessagingProvider : ICloudMessagingProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAwsReceiver _receiver;
    private readonly SimulatedAwsSender _sender;

    /// <summary>Initializes a new instance of <see cref="SimulatedAwsMessagingProvider"/>.</summary>
    public SimulatedAwsMessagingProvider(
        ISimulatorStore store, SimulatedAwsReceiver receiver, SimulatedAwsSender sender)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Aws;

    /// <inheritdoc/>
    public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) =>
        Task.FromResult(Result.Success());

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        var entities = _store.GetEntities(namespaceId)
            .Select(e => new CloudEntity
            {
                Name = e.Name,
                EntityType = e.EntityType,
                ActiveMessageCount = e.GetMessageCount(),
                DeadLetterCount = e.GetDlqCount(),
                Provider = CloudProviderType.Aws,
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<CloudEntity>>.Success(entities));
    }

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _receiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _sender;
}
