using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Shared.Constants;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Coordinates message operations by resolving the namespace's cloud provider
/// and delegating to the provider-specific sender/receiver implementations.
/// </summary>
public sealed class MessageOperationsService : IMessageOperationsService
{
    private readonly CloudProviderRouter _router;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<MessageOperationsService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MessageOperationsService"/>.
    /// </summary>
    /// <param name="router">Router used to resolve cloud providers.</param>
    /// <param name="namespaceRepository">Repository for looking up namespace metadata.</param>
    /// <param name="logger">Logger instance.</param>
    public MessageOperationsService(
        CloudProviderRouter router,
        INamespaceRepository namespaceRepository,
        ILogger<MessageOperationsService> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a single message to the namespace/entity described in <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The send request containing namespace, entity and payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        return SendInternalAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a batch of messages to a single namespace/entity. All requests in the batch
    /// must target the same namespace.
    /// </summary>
    /// <param name="requests">The collection of send requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Task<Result> SendBatchAsync(IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        return SendBatchInternalAsync(requests, cancellationToken);
    }

    /// <summary>
    /// Peeks messages from the active queue/subscription without removing them.
    /// </summary>
    /// <param name="request">Request describing namespace and entity to peek from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of messages or a failure result.</returns>
    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        return PeekMessagesInternalAsync(request, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<Message>>> PeekMessagesInternalAsync(GetMessagesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: PeekMessages", ns.Id, ns.Provider);

            var receiver = GetReceiver(provider);
            var result = await receiver.PeekMessagesAsync(request, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConvertExceptionToResult<IReadOnlyList<Message>>(ex, ErrorCodes.Message.ReceiveFailed);
        }
    }

    /// <summary>
    /// Peeks messages from the dead-letter queue for the specified entity.
    /// </summary>
    /// <param name="request">Request describing namespace and entity to peek from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of dead-letter messages or a failure result.</returns>
    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        return PeekDeadLetterMessagesInternalAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the approximate message count for the specified entity.
    /// </summary>
    /// <param name="namespaceId">Namespace identifier.</param>
    /// <param name="entityName">Entity (queue/topic) name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message count or a failure result.</returns>
    public Task<Result<long>> GetMessageCountAsync(Guid namespaceId, string entityName, string? subscriptionName = null, CancellationToken cancellationToken = default)
    {
        return GetMessageCountInternalAsync(namespaceId, entityName, subscriptionName, cancellationToken);
    }

    /// <summary>
    /// Moves messages matching the request to the dead-letter queue.
    /// </summary>
    /// <param name="request">Dead letter request describing namespace/entity and filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages moved or a failure result.</returns>
    public Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default)
    {
        return DeadLetterMessagesInternalAsync(request, cancellationToken);
    }

    /// <summary>
    /// Replays a single message from the dead-letter queue back to the active queue.
    /// </summary>
    /// <param name="namespaceId">Namespace identifier.</param>
    /// <param name="entityName">Entity (queue/topic) name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="sequenceNumber">Sequence number of the message to replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Task<Result> ReplayMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, CancellationToken cancellationToken = default)
    {
        return ReplayMessageInternalAsync(namespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken);
    }

    /// <summary>
    /// Purges a message from either the active or dead-letter queue.
    /// </summary>
    /// <param name="namespaceId">Namespace identifier.</param>
    /// <param name="entityName">Entity (queue/topic) name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="sequenceNumber">Sequence number of the message to purge.</param>
    /// <param name="fromDeadLetter">If true, purge from dead-letter; otherwise purge from active queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Task<Result> PurgeMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default)
    {
        return PurgeMessageInternalAsync(namespaceId, entityName, subscriptionName, sequenceNumber, fromDeadLetter, cancellationToken);
    }

    /// <summary>
    /// Retrieves scheduled messages for the specified entity.
    /// </summary>
    /// <param name="namespaceId">Namespace identifier.</param>
    /// <param name="entityName">Entity (queue/topic) name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="maxMessages">Maximum number of scheduled messages to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of scheduled messages or a failure result.</returns>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken = default)
    {
        return GetScheduledMessagesInternalAsync(namespaceId, entityName, subscriptionName, maxMessages, cancellationToken);
    }

    /// <summary>
    /// Resolves the <see cref="Namespace"/> and its registered <see cref="ICloudMessagingProvider"/>.
    /// Throws <see cref="InvalidOperationException"/> when the namespace cannot be found or no provider
    /// is registered for the namespace's provider type.
    /// </summary>
    /// <param name="namespaceId">Namespace identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of resolved <see cref="Namespace"/> and <see cref="ICloudMessagingProvider"/>.</returns>
    private async Task<(Namespace Namespace, ICloudMessagingProvider Provider)> ResolveProviderAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        if (namespaceId == Guid.Empty)
            throw new ArgumentException("NamespaceId must be provided", nameof(namespaceId));

        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
        {
            var msg = nsResult.Error?.Message ?? "Namespace lookup failed";
            throw new NamespaceResolutionException($"Failed to resolve Namespace '{namespaceId}': {msg}");
        }

        var ns = nsResult.Value;

        if (!_router.IsRegistered(ns.Provider))
        {
            throw new ProviderNotRegisteredException(
                $"The '{ns.Provider}' cloud provider is not enabled on this server (namespace: {namespaceId}). " +
                $"Set 'CloudProviders:{ns.Provider}:Enabled' to 'true' in appsettings and restart.");
        }

        var provider = _router.Resolve(ns.Provider);
        return (ns, provider);
    }

    // Centralized exception -> Result mapping helpers to avoid duplicated logic
    private static Result<T> ConvertExceptionToResult<T>(Exception ex, string externalErrorCode)
    {
        if (ex is NamespaceResolutionException)
            return Result.Failure<T>(Error.NotFound(ErrorCodes.Namespace.NotFound, ex.Message));

        if (ex is InvalidOperationException)
            return Result.Failure<T>(Error.ExternalService(externalErrorCode, ex.Message ?? "Provider resolution failed"));

        return Result.Failure<T>(Error.Internal(ErrorCodes.General.UnexpectedError, ex.Message));
    }

    private static Result ConvertExceptionToResult(Exception ex, string externalErrorCode)
    {
        if (ex is NamespaceResolutionException)
            return Result.Failure(Error.NotFound(ErrorCodes.Namespace.NotFound, ex.Message));

        if (ex is InvalidOperationException)
            return Result.Failure(Error.ExternalService(externalErrorCode, ex.Message ?? "Provider resolution failed"));

        return Result.Failure(Error.Internal(ErrorCodes.General.UnexpectedError, ex.Message));
    }

    /// <summary>
    /// Returns an <see cref="IMessageReceiver"/> from the resolved provider.
    /// </summary>
    /// <param name="provider">Resolved cloud provider.</param>
    /// <returns>The provider's message receiver implementation.</returns>
    private IMessageReceiver GetReceiver(ICloudMessagingProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        return provider.GetMessageReceiver();
    }

    /// <summary>
    /// Returns an <see cref="IMessageSender"/> from the resolved provider.
    /// </summary>
    /// <param name="provider">Resolved cloud provider.</param>
    /// <returns>The provider's message sender implementation.</returns>
    private IMessageSender GetSender(ICloudMessagingProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        return provider.GetMessageSender();
    }

    // Internal implementations that delegate to provider implementations
    private async Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesInternalAsync(GetMessagesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: PeekDeadLetterMessages", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.PeekDeadLetterMessagesAsync(request, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ConvertExceptionToResult<IReadOnlyList<Message>>(ex, ErrorCodes.Message.ReceiveFailed);
            }
    }

    private async Task<Result<long>> GetMessageCountInternalAsync(Guid namespaceId, string entityName, string? subscriptionName, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(namespaceId, cancellationToken).ConfigureAwait(false);

            if (!provider.Capabilities.SupportsMessageCounts)
            {
                return Result<long>.Failure(Error.Validation(
                    ErrorCodes.Message.CountUnsupported,
                    $"Message count queries are not supported for {ns.Provider}. " +
                    $"Supported providers: Azure, AWS. Current provider: {ns.Provider}. " +
                    $"See provider capabilities: {provider.Capabilities.Notes}"));
            }

            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: GetMessageCount", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.GetMessageCountAsync(namespaceId, entityName, subscriptionName, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex)
            {
                return ConvertExceptionToResult<long>(ex, ErrorCodes.Message.ReceiveFailed);
            }
    }

    private async Task<Result<int>> DeadLetterMessagesInternalAsync(DeadLetterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);

            if (!provider.Capabilities.SupportsManualDeadLetter)
            {
                return Result<int>.Failure(Error.Validation(
                    ErrorCodes.Message.DeadLetterUnsupported,
                    $"Manual dead-lettering is not supported for {ns.Provider}. " +
                    $"Supported providers: Azure, AWS. Current provider: {ns.Provider}. " +
                    $"See provider capabilities: {provider.Capabilities.Notes}"));
            }

            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: DeadLetterMessages", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.DeadLetterMessagesAsync(request, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex)
            {
                return ConvertExceptionToResult<int>(ex, ErrorCodes.Message.ReceiveFailed);
            }
    }

    private async Task<Result> ReplayMessageInternalAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(namespaceId, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: ReplayMessage", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.ReplayMessageAsync(namespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex)
            {
                return ConvertExceptionToResult(ex, ErrorCodes.Message.ReceiveFailed);
            }
    }

    private async Task<Result> PurgeMessageInternalAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(namespaceId, cancellationToken).ConfigureAwait(false);

            if (!provider.Capabilities.SupportsPurge)
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Message.PurgeUnsupported,
                    $"Purge is not supported for {ns.Provider}. " +
                    $"Supported providers: AWS, GCP. Current provider: {ns.Provider}. " +
                    $"See provider capabilities: {provider.Capabilities.Notes}"));
            }

            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: PurgeMessage", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.PurgeMessageAsync(namespaceId, entityName, subscriptionName, sequenceNumber, fromDeadLetter, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ConvertExceptionToResult(ex, ErrorCodes.Message.ReceiveFailed);
        }
    }

    private async Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesInternalAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(namespaceId, cancellationToken).ConfigureAwait(false);

            if (!provider.Capabilities.SupportsScheduledMessages)
            {
                return Result<IReadOnlyList<Message>>.Failure(Error.Validation(
                    ErrorCodes.Message.ScheduledUnsupported,
                    $"Scheduled messages are not supported for {ns.Provider}. " +
                    $"Supported providers: Azure. Current provider: {ns.Provider}. " +
                    $"See provider capabilities: {provider.Capabilities.Notes}"));
            }

            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: GetScheduledMessages", ns.Id, ns.Provider);
            var receiver = GetReceiver(provider);
            return await receiver.GetScheduledMessagesAsync(namespaceId, entityName, subscriptionName, maxMessages, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ConvertExceptionToResult<IReadOnlyList<Message>>(ex, ErrorCodes.Message.ScheduledListFailed);
        }
    }

    private async Task<Result> SendInternalAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.NamespaceId is null || request.NamespaceId == Guid.Empty)
            {
                return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound, "Namespace ID is required."));
            }

            var (ns, provider) = await ResolveProviderAsync(request.NamespaceId.Value, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: Send", ns.Id, ns.Provider);
            var sender = GetSender(provider);
            return await sender.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex)
            {
                return ConvertExceptionToResult(ex, ErrorCodes.Message.SendFailed);
            }
    }

    private async Task<Result> SendBatchInternalAsync(IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken)
    {
        try
        {
            var requestList = requests?.ToList() ?? new List<SendMessageRequest>();
            if (requestList.Count == 0)
                return Result.Success();

            var first = requestList[0];
            if (first.NamespaceId is null || first.NamespaceId == Guid.Empty)
            {
                return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound, "Namespace ID is required for batch send."));
            }

            for (var i = 1; i < requestList.Count; i++)
            {
                if (requestList[i].NamespaceId != first.NamespaceId)
                {
                    return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound,
                        $"All requests in a batch must target the same namespace. Entry at index {i} targets a different namespace."));
                }
            }

            var (ns, provider) = await ResolveProviderAsync(first.NamespaceId.Value, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("NamespaceId: {NamespaceId}, Provider: {Provider}, Operation: SendBatch", ns.Id, ns.Provider);
            var sender = GetSender(provider);
            return await sender.SendBatchAsync(requestList, cancellationToken).ConfigureAwait(false);
        }
            catch (Exception ex)
            {
                return ConvertExceptionToResult(ex, ErrorCodes.Message.SendFailed);
            }
    }
}
