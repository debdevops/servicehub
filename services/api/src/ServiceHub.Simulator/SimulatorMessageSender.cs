using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Providers.Aws;
using ServiceHub.Simulator.Providers.Azure;
using ServiceHub.Simulator.Providers.Gcp;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// An <see cref="IMessageSender"/> implementation that routes to the correct
/// simulated sender based on the namespace ID stored in
/// <see cref="ISimulatorStore"/>.
/// </summary>
public sealed class SimulatorMessageSender : IMessageSender
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAzureSender _azure;
    private readonly SimulatedAwsSender _aws;
    private readonly SimulatedGcpSender _gcp;

    /// <summary>Initializes a new instance of <see cref="SimulatorMessageSender"/>.</summary>
    public SimulatorMessageSender(
        ISimulatorStore store,
        SimulatedAzureSender azure,
        SimulatedAwsSender aws,
        SimulatedGcpSender gcp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _azure = azure ?? throw new ArgumentNullException(nameof(azure));
        _aws = aws ?? throw new ArgumentNullException(nameof(aws));
        _gcp = gcp ?? throw new ArgumentNullException(nameof(gcp));
    }

    /// <inheritdoc/>
    public Task<Result> SendAsync(
        SendMessageRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).SendAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> SendBatchAsync(
        IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        var list = requests?.ToList();
        if (list is null || list.Count == 0)
            return Task.FromResult(Result.Success());
        return Resolve(list[0].NamespaceId).SendBatchAsync(list, cancellationToken);
    }

    private IMessageSender Resolve(Guid namespaceId)
    {
        var ns = _store.GetNamespace(namespaceId);
        if (ns is null) return _azure;

        return ns.Provider switch
        {
            CloudProviderType.Aws => _aws,
            CloudProviderType.Gcp => _gcp,
            _ => _azure,
        };
    }
}


namespace ServiceHub.Simulator;

/// <summary>
/// An <see cref="IMessageSender"/> implementation that routes to the correct
/// simulated sender based on the namespace ID stored in
/// <see cref="ISimulatorStore"/>.
/// </summary>
public sealed class SimulatorMessageSender : IMessageSender
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAzureSender _azure;
    private readonly SimulatedAwsSender _aws;
    private readonly SimulatedGcpSender _gcp;

    /// <summary>Initializes a new instance of <see cref="SimulatorMessageSender"/>.</summary>
    public SimulatorMessageSender(
        ISimulatorStore store,
        SimulatedAzureSender azure,
        SimulatedAwsSender aws,
        SimulatedGcpSender gcp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _azure = azure ?? throw new ArgumentNullException(nameof(azure));
        _aws = aws ?? throw new ArgumentNullException(nameof(aws));
        _gcp = gcp ?? throw new ArgumentNullException(nameof(gcp));
    }

    /// <inheritdoc/>
    public Task<Result> SendAsync(
        SendMessageRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).SendAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> SendBatchAsync(
        IReadOnlyList<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests is null || requests.Count == 0)
            return Task.FromResult(Result.Success());
        return Resolve(requests[0].NamespaceId).SendBatchAsync(requests, cancellationToken);
    }

    private IMessageSender Resolve(Guid namespaceId)
    {
        var ns = _store.GetNamespace(namespaceId);
        if (ns is null) return _azure;

        return ns.Provider switch
        {
            CloudProviderType.Aws => _aws,
            CloudProviderType.Gcp => _gcp,
            _ => _azure,
        };
    }
}
