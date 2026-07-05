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

public sealed class MessageOperationsService : IMessageOperationsService
{
    private readonly CloudProviderRouter _router;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<MessageOperationsService> _logger;

    public MessageOperationsService(
        CloudProviderRouter router,
        INamespaceRepository namespaceRepository,
        ILogger<MessageOperationsService> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        return SendInternalAsync(request, cancellationToken);
    }

    public Task<Result> SendBatchAsync(IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        return SendBatchInternalAsync(requests, cancellationToken);
    }

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
        catch (Exception ex)
        {
            return ConvertExceptionToResult<IReadOnlyList<Message>>(ex, ErrorCodes.Message.ReceiveFailed);
        }
    }

    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        return PeekDeadLetterMessagesInternalAsync(request, cancellationToken);
    }

    public Task<Result<long>> GetMessageCountAsync(Guid namespaceId, string entityName, string? subscriptionName = null, CancellationToken cancellationToken = default)
    {
        return GetMessageCountInternalAsync(namespaceId, entityName, subscriptionName, cancellationToken);
    }

    public Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default)
    {
        return DeadLetterMessagesInternalAsync(request, cancellationToken);
    }

    public Task<Result> ReplayMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, CancellationToken cancellationToken = default)
    {
        return ReplayMessageInternalAsync(namespaceId, entityName, subscriptionName, sequenceNumber, cancellationToken);
    }

    public Task<Result> PurgeMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default)
    {
        return PurgeMessageInternalAsync(namespaceId, entityName, subscriptionName, sequenceNumber, fromDeadLetter, cancellationToken);
    }

    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken = default)
    {
        return GetScheduledMessagesInternalAsync(namespaceId, entityName, subscriptionName, maxMessages, cancellationToken);
    }

    // Private helpers (skeletons)
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
            throw new InvalidOperationException($"Failed to resolve Namespace '{namespaceId}': {msg}");
        }

        var ns = nsResult.Value;

        try
        {
            var provider = _router.Resolve(ns.Provider);
            return (ns, provider);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"No ICloudMessagingProvider registered for provider '{ns.Provider}' (namespace: {namespaceId}). {ex.Message}", ex);
        }
    }

    // Centralized exception -> Result mapping helpers to avoid duplicated logic
    private static Result<T> ConvertExceptionToResult<T>(Exception ex, string externalErrorCode)
    {
        if (ex is InvalidOperationException)
        {
            var message = ex.Message ?? "Provider resolution failed";
            if (message.Contains("Namespace", StringComparison.OrdinalIgnoreCase))
                return Result.Failure<T>(Error.NotFound(ErrorCodes.Namespace.NotFound, message));
            return Result.Failure<T>(Error.ExternalService(externalErrorCode, message));
        }

        return Result.Failure<T>(Error.Internal(ErrorCodes.General.UnexpectedError, ex.Message));
    }

    private static Result ConvertExceptionToResult(Exception ex, string externalErrorCode)
    {
        if (ex is InvalidOperationException)
        {
            var message = ex.Message ?? "Provider resolution failed";
            if (message.Contains("Namespace", StringComparison.OrdinalIgnoreCase))
                return Result.Failure(Error.NotFound(ErrorCodes.Namespace.NotFound, message));
            return Result.Failure(Error.ExternalService(externalErrorCode, message));
        }

        return Result.Failure(Error.Internal(ErrorCodes.General.UnexpectedError, ex.Message));
    }

    private IMessageReceiver GetReceiver(ICloudMessagingProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        return provider.GetMessageReceiver();
    }

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
            catch (Exception ex)
            {
                return ConvertExceptionToResult<IReadOnlyList<Message>>(ex, ErrorCodes.Message.ReceiveFailed);
            }
    }

    private async Task<Result<long>> GetMessageCountInternalAsync(Guid namespaceId, string entityName, string? subscriptionName, CancellationToken cancellationToken)
    {
        try
        {
            var (ns, provider) = await ResolveProviderAsync(namespaceId, cancellationToken).ConfigureAwait(false);
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
