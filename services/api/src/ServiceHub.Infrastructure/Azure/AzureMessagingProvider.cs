using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Azure;

/// <summary>
/// <see cref="ICloudMessagingProvider"/> implementation for Microsoft Azure Service Bus.
/// Wraps the existing <see cref="IServiceBusClientFactory"/>, <see cref="IMessageReceiver"/>,
/// and <see cref="IMessageSender"/> infrastructure without duplicating their logic.
/// </summary>
public sealed class AzureMessagingProvider : ICloudMessagingProvider
{
    private readonly IServiceBusClientFactory _clientFactory;
    private readonly IMessageReceiver _messageReceiver;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<AzureMessagingProvider> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="AzureMessagingProvider"/>.
    /// </summary>
    /// <param name="clientFactory">Factory used to validate and create Service Bus clients.</param>
    /// <param name="messageReceiver">The Service Bus message receiver.</param>
    /// <param name="messageSender">The Service Bus message sender.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public AzureMessagingProvider(
        IServiceBusClientFactory clientFactory,
        IMessageReceiver messageReceiver,
        IMessageSender messageSender,
        ILogger<AzureMessagingProvider> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Azure;

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates to <see cref="IServiceBusClientFactory.CreateClientAsync"/> which validates
    /// the connection string format and attempts to establish an SDK client.
    /// </remarks>
    public Task<Result> ValidateConnectionAsync(Core.Entities.Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        _logger.LogDebug("Validating Azure Service Bus connection for namespace {NamespaceId}", ns.Id);
        return _clientFactory.CreateClientAsync(ns, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// TODO (Phase 2): integrate with the Azure Service Bus Administration SDK to enumerate
    /// queues, topics, and subscriptions dynamically.
    /// For Phase 1 an empty list is returned so the router and response pipeline can be exercised.
    /// </remarks>
    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        _logger.LogDebug(
            "ListEntitiesAsync called for Azure namespace {NamespaceId} — returning empty list (Phase 1 stub)",
            namespaceId);

        IReadOnlyList<CloudEntity> empty = [];
        return Task.FromResult(Result<IReadOnlyList<CloudEntity>>.Success(empty));
    }

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _messageReceiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _messageSender;
}
