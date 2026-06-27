using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ServiceHub.Core.Interfaces;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// Simulated implementation of <see cref="IServiceBusClientCache"/> that returns
/// thread-safe in-memory client wrappers for the simulator.
/// </summary>
public sealed class SimulatorClientCache : IServiceBusClientCache
{
    private readonly ConcurrentDictionary<Guid, IServiceBusClientWrapper> _clients = new();
    private readonly ISimulatorStore _store;
    private readonly IMessageReceiver _messageReceiver;
    private readonly IMessageSender _messageSender;

    /// <summary>
    /// Initializes a new instance of <see cref="SimulatorClientCache"/>.
    /// </summary>
    public SimulatorClientCache(
        ISimulatorStore store,
        IMessageReceiver messageReceiver,
        IMessageSender messageSender)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
    }

    /// <inheritdoc/>
    public IServiceBusClientWrapper GetOrCreate(Guid namespaceId, string connectionString)
    {
        return _clients.GetOrAdd(namespaceId, id =>
            new SimulatorClientWrapper(id, _store, _messageReceiver, _messageSender));
    }

    /// <inheritdoc/>
    public Task RemoveAsync(Guid namespaceId, CancellationToken cancellationToken = default)
    {
        _clients.TryRemove(namespaceId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool Contains(Guid namespaceId)
    {
        return _clients.ContainsKey(namespaceId);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _clients.Clear();
        return ValueTask.CompletedTask;
    }
}
