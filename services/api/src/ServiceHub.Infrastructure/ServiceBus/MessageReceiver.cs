using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;
using Azure.Messaging.ServiceBus;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Receives and peeks messages from Azure Service Bus queues and subscriptions.
/// </summary>
public sealed class MessageReceiver : IMessageReceiver
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<MessageReceiver> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageReceiver"/> class.
    /// </summary>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger instance.</param>
    public MessageReceiver(
        IServiceBusClientCache clientCache,
        INamespaceRepository namespaceRepository,
        ILogger<MessageReceiver> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<ServiceBusException>(ex =>
                    ex.IsTransient ||
                    ex.Reason == ServiceBusFailureReason.ServiceBusy ||
                    ex.Reason == ServiceBusFailureReason.ServiceTimeout ||
                    ex.Reason == ServiceBusFailureReason.ServiceCommunicationProblem),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry attempt {AttemptNumber} for peek operation after {Delay}ms. Exception: {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateRequest(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<Message>>(validationResult.Errors);
        }

        var clientResult = await GetClientWrapperAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (clientResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<Message>>(clientResult.Error);
        }

        try
        {
            var peekRequest = request with { FromDeadLetter = false };
            
            var result = await _resiliencePipeline.ExecuteAsync(async ct =>
                await clientResult.Value.PeekMessagesAsync(peekRequest, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result;
            }

            _logger.LogDebug(
                "Peeked {Count} messages from {EntityName} in namespace {NamespaceId}",
                result.Value.Count,
                request.EntityName,
                request.NamespaceId);

            return result;
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Failed to peek messages from {EntityName} after retries",
                request.EntityName);

            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                ErrorCodes.Message.ReceiveFailed,
                $"Failed to peek messages after retries: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error peeking messages");

            return Result.Failure<IReadOnlyList<Message>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while peeking messages."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateRequest(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<Message>>(validationResult.Errors);
        }

        var clientResult = await GetClientWrapperAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (clientResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<Message>>(clientResult.Error);
        }

        try
        {
            var dlqRequest = request with { FromDeadLetter = true };
            
            var result = await _resiliencePipeline.ExecuteAsync(async ct =>
                await clientResult.Value.PeekMessagesAsync(dlqRequest, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result;
            }

            _logger.LogDebug(
                "Peeked {Count} dead-letter messages from {EntityName} in namespace {NamespaceId}",
                result.Value.Count,
                request.EntityName,
                request.NamespaceId);

            return result;
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Failed to peek dead-letter messages from {EntityName} after retries",
                request.EntityName);

            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                ErrorCodes.Message.ReceiveFailed,
                $"Failed to peek dead-letter messages after retries: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error peeking dead-letter messages");

            return Result.Failure<IReadOnlyList<Message>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while peeking dead-letter messages."));
        }
    }

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        if (namespaceId == Guid.Empty)
        {
            return Task.FromResult(Result.Failure<long>(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace ID is required.")));
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            return Task.FromResult(Result.Failure<long>(Error.Validation(
                ErrorCodes.Message.QueueNameRequired,
                "Queue or topic name is required.")));
        }

        // Message count requires ServiceBusAdministrationClient which needs the connection string
        // For now, return an indication that this feature requires admin access
        _logger.LogWarning(
            "GetMessageCountAsync is not yet implemented for namespace {NamespaceId}, entity {EntityName}",
            namespaceId,
            entityName);

        return Task.FromResult(Result.Failure<long>(Error.Internal(
            ErrorCodes.General.UnexpectedError,
            "Message count retrieval is not yet implemented. This feature requires the Service Bus Administration API.")));
    }

    private async Task<Result<IServiceBusClientWrapper>> GetClientWrapperAsync(
        Guid namespaceId,
        CancellationToken cancellationToken)
    {
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken)
            .ConfigureAwait(false);

        if (namespaceResult.IsFailure)
        {
            return Result.Failure<IServiceBusClientWrapper>(namespaceResult.Error);
        }

        var @namespace = namespaceResult.Value;

        if (string.IsNullOrWhiteSpace(@namespace.ConnectionString))
        {
            return Result.Failure<IServiceBusClientWrapper>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Namespace connection string is not configured."));
        }

        var clientWrapper = _clientCache.GetOrCreate(@namespace.Id, @namespace.ConnectionString);
        return Result.Success(clientWrapper);
    }

    private static Result ValidateRequest(GetMessagesRequest request)
    {
        var errors = new List<Error>();

        if (request.NamespaceId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Message.QueueNameRequired,
                "Queue or topic name is required."));
        }

        if (request.MaxMessages < GetMessagesRequest.MinAllowedMessages ||
            request.MaxMessages > GetMessagesRequest.MaxAllowedMessages)
        {
            errors.Add(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                $"MaxMessages must be between {GetMessagesRequest.MinAllowedMessages} and {GetMessagesRequest.MaxAllowedMessages}."));
        }

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }
}
