using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator.Providers.Aws;
using ServiceHub.Simulator.Providers.Azure;
using ServiceHub.Simulator.Providers.Gcp;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// An <see cref="IMessageReceiver"/> implementation that routes to the correct
/// simulated receiver based on the namespace ID stored in
/// <see cref="ISimulatorStore"/>.
/// </summary>
public sealed class SimulatorMessageReceiver :
    IMessageReceiver,
    IVisibilityStatusProvider,
    IAckDeadlineStatusProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAzureReceiver _azure;
    private readonly SimulatedAwsReceiver _aws;
    private readonly SimulatedGcpReceiver _gcp;

    /// <summary>Initializes a new instance of <see cref="SimulatorMessageReceiver"/>.</summary>
    public SimulatorMessageReceiver(
        ISimulatorStore store,
        SimulatedAzureReceiver azure,
        SimulatedAwsReceiver aws,
        SimulatedGcpReceiver gcp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _azure = azure ?? throw new ArgumentNullException(nameof(azure));
        _aws = aws ?? throw new ArgumentNullException(nameof(aws));
        _gcp = gcp ?? throw new ArgumentNullException(nameof(gcp));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).PeekMessagesAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).PeekDeadLetterMessagesAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId, string entityName, string? subscriptionName = null,
        CancellationToken cancellationToken = default)
        => Resolve(namespaceId).GetMessageCountAsync(namespaceId, entityName, subscriptionName, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).DeadLetterMessagesAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> ReplayMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, CancellationToken cancellationToken = default)
        => Resolve(namespaceId).ReplayMessageAsync(namespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> PurgeMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, bool fromDeadLetter,
        CancellationToken cancellationToken = default)
        => Resolve(namespaceId).PurgeMessageAsync(namespaceId, entityName, subscriptionName, sequenceNumber, fromDeadLetter, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        int maxMessages, CancellationToken cancellationToken = default)
        => Resolve(namespaceId).GetScheduledMessagesAsync(namespaceId, entityName, subscriptionName, maxMessages, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SqsVisibilityInfo>> GetVisibilityWindowStatusAsync(
        Guid namespaceId, string queueName, CancellationToken cancellationToken = default)
        => _aws.GetVisibilityWindowStatusAsync(namespaceId, queueName, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<GcpAckDeadlineStatus>> GetAckDeadlineStatusAsync(
        Guid namespaceId, string subscriptionId, CancellationToken cancellationToken = default)
        => _gcp.GetAckDeadlineStatusAsync(namespaceId, subscriptionId, cancellationToken);

    // ── Private helpers ───────────────────────────────────────────────────────

    private IMessageReceiver Resolve(Guid namespaceId)
    {
        var ns = _store.GetNamespace(namespaceId);
        if (ns is null)
        {
            // Fall back to Azure (namespace may have been added via NamespacesController)
            return _azure;
        }

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
/// An <see cref="IMessageReceiver"/> implementation that routes to the correct
/// simulated receiver based on the namespace ID stored in
/// <see cref="ISimulatorStore"/>.
/// </summary>
public sealed class SimulatorMessageReceiver :
    IMessageReceiver,
    IVisibilityStatusProvider,
    IAckDeadlineStatusProvider
{
    private readonly ISimulatorStore _store;
    private readonly SimulatedAzureReceiver _azure;
    private readonly SimulatedAwsReceiver _aws;
    private readonly SimulatedGcpReceiver _gcp;

    /// <summary>Initializes a new instance of <see cref="SimulatorMessageReceiver"/>.</summary>
    public SimulatorMessageReceiver(
        ISimulatorStore store,
        SimulatedAzureReceiver azure,
        SimulatedAwsReceiver aws,
        SimulatedGcpReceiver gcp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _azure = azure ?? throw new ArgumentNullException(nameof(azure));
        _aws = aws ?? throw new ArgumentNullException(nameof(aws));
        _gcp = gcp ?? throw new ArgumentNullException(nameof(gcp));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<MessageResponse>>> PeekMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).PeekMessagesAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<MessageResponse>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.NamespaceId).PeekDeadLetterMessagesAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId, string entityName, string? subscriptionName = null,
        CancellationToken cancellationToken = default)
        => Resolve(namespaceId).GetMessageCountAsync(namespaceId, entityName, subscriptionName, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<int>> DeadLetterMessagesAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        int maxMessages, CancellationToken cancellationToken = default)
        => Resolve(namespaceId).DeadLetterMessagesAsync(namespaceId, entityName, subscriptionName, maxMessages, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> ReplayMessageAsync(
        Guid namespaceId, string entityName, string? subscriptionName,
        long sequenceNumber, CancellationToken cancellationToken = default)
        => Resolve(namespaceId).ReplayMessageAsync(namespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken);

    /// <inheritdoc/>
    public Task<Result> PurgeMessageAsync(
        Guid namespaceId, string entityName, long sequenceNumber, bool fromDeadLetter,
        CancellationToken cancellationToken = default)
        => Resolve(namespaceId).PurgeMessageAsync(namespaceId, entityName, sequenceNumber, fromDeadLetter, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<MessageResponse>>> GetScheduledMessagesAsync(
        Guid namespaceId, string entityName, int maxMessages,
        CancellationToken cancellationToken = default)
        => Resolve(namespaceId).GetScheduledMessagesAsync(namespaceId, entityName, maxMessages, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SqsVisibilityInfo>> GetVisibilityWindowStatusAsync(
        Guid namespaceId, string queueName, CancellationToken cancellationToken = default)
        => _aws.GetVisibilityWindowStatusAsync(namespaceId, queueName, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<GcpAckDeadlineStatus>> GetAckDeadlineStatusAsync(
        Guid namespaceId, string subscriptionId, CancellationToken cancellationToken = default)
        => _gcp.GetAckDeadlineStatusAsync(namespaceId, subscriptionId, cancellationToken);

    // ── Private helpers ───────────────────────────────────────────────────────

    private IMessageReceiver Resolve(Guid namespaceId)
    {
        var ns = _store.GetNamespace(namespaceId);
        if (ns is null)
        {
            // Fall back to Azure (namespace may have been added via NamespacesController)
            return _azure;
        }

        return ns.Provider switch
        {
            CloudProviderType.Aws => _aws,
            CloudProviderType.Gcp => _gcp,
            _ => _azure,
        };
    }
}
