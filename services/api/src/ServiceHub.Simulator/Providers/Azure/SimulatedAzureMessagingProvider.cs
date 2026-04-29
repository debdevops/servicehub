using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator.Providers.Azure;

/// <summary>
/// Simulated Azure Service Bus messaging provider.
/// Validates connections instantly and lists entities from the in-memory store.
/// </summary>
public sealed class SimulatedAzureMessagingProvider : ICloudMessagingProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAzureReceiver _receiver;
    private readonly SimulatedAzureSender _sender;

    /// <summary>Initializes a new instance of <see cref="SimulatedAzureMessagingProvider"/>.</summary>
    public SimulatedAzureMessagingProvider(
        ISimulatorStore store, SimulatedAzureReceiver receiver, SimulatedAzureSender sender)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Azure;

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
                Provider = CloudProviderType.Azure,
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<CloudEntity>>.Success(entities));
    }

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _receiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _sender;
}
