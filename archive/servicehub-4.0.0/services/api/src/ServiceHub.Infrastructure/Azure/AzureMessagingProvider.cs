using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
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
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly IServiceBusClientCache _clientCache;
    private readonly ILogger<AzureMessagingProvider> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="AzureMessagingProvider"/>.
    /// </summary>
    /// <param name="clientFactory">Factory used to validate and create Service Bus clients.</param>
    /// <param name="messageReceiver">The Service Bus message receiver.</param>
    /// <param name="messageSender">The Service Bus message sender.</param>
    /// <param name="namespaceRepository">Repository for looking up namespace metadata.</param>
    /// <param name="connectionStringProtector">Protector used to decrypt stored connection strings.</param>
    /// <param name="clientCache">Cache providing Service Bus client wrappers per namespace.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public AzureMessagingProvider(
        IServiceBusClientFactory clientFactory,
        IMessageReceiver messageReceiver,
        IMessageSender messageSender,
        INamespaceRepository namespaceRepository,
        IConnectionStringProtector connectionStringProtector,
        IServiceBusClientCache clientCache,
        ILogger<AzureMessagingProvider> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Azure;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities => ProviderCapabilities.Azure;

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
    /// Enumerates queues, topics, and subscriptions via the cached
    /// <see cref="IServiceBusClientWrapper"/> for the namespace. Subscription entities are
    /// named with their full path (<c>topic/subscriptions/subscription</c>).
    /// </remarks>
    public async Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CloudEntity>>(nsResult.Error);

        var ns = nsResult.Value;
        if (ns.ConnectionString is null)
            return Result.Failure<IReadOnlyList<CloudEntity>>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Namespace does not have a connection string configured."));

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
            return Result.Failure<IReadOnlyList<CloudEntity>>(unprotectResult.Error);

        try
        {
            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
            var entities = new List<CloudEntity>();

            var queuesResult = await wrapper.GetQueuesAsync(ct).ConfigureAwait(false);
            if (queuesResult.IsFailure)
                return Result.Failure<IReadOnlyList<CloudEntity>>(queuesResult.Error);

            foreach (var queue in queuesResult.Value)
            {
                entities.Add(new CloudEntity
                {
                    Name = queue.Name,
                    EntityType = "Queue",
                    ActiveMessageCount = queue.ActiveMessageCount,
                    DeadLetterCount = queue.DeadLetterMessageCount,
                    Provider = CloudProviderType.Azure,
                });
            }

            var topicsResult = await wrapper.GetTopicsAsync(ct).ConfigureAwait(false);
            if (topicsResult.IsFailure)
                return Result.Failure<IReadOnlyList<CloudEntity>>(topicsResult.Error);

            foreach (var topic in topicsResult.Value)
            {
                entities.Add(new CloudEntity
                {
                    Name = topic.Name,
                    EntityType = "Topic",
                    Provider = CloudProviderType.Azure,
                });

                var subscriptionsResult = await wrapper.GetSubscriptionsAsync(topic.Name, ct).ConfigureAwait(false);
                if (subscriptionsResult.IsFailure)
                    return Result.Failure<IReadOnlyList<CloudEntity>>(subscriptionsResult.Error);

                foreach (var subscription in subscriptionsResult.Value)
                {
                    entities.Add(new CloudEntity
                    {
                        Name = $"{topic.Name}/subscriptions/{subscription.Name}",
                        EntityType = "Subscription",
                        ActiveMessageCount = subscription.ActiveMessageCount,
                        DeadLetterCount = subscription.DeadLetterMessageCount,
                        Provider = CloudProviderType.Azure,
                    });
                }
            }

            _logger.LogDebug(
                "Listed {EntityCount} entities for Azure namespace {NamespaceId}",
                entities.Count,
                namespaceId);

            return Result<IReadOnlyList<CloudEntity>>.Success(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list entities for Azure namespace {NamespaceId}", namespaceId);
            return Result.Failure<IReadOnlyList<CloudEntity>>(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed, ex.Message));
        }
    }

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _messageReceiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _messageSender;
}
