using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;
using Azure.Messaging.ServiceBus;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Sends messages to Azure Service Bus queues and topics with retry support.
/// </summary>
public sealed class MessageSender : IMessageSender
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<MessageSender> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageSender"/> class.
    /// </summary>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger instance.</param>
    public MessageSender(
        IServiceBusClientCache clientCache,
        INamespaceRepository namespaceRepository,
        ILogger<MessageSender> logger)
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
                        "Retry attempt {AttemptNumber} for Service Bus operation after {Delay}ms. Exception: {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    /// <inheritdoc/>
    public async Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateRequest(request);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var namespaceResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken)
            .ConfigureAwait(false);

        if (namespaceResult.IsFailure)
        {
            return Result.Failure(namespaceResult.Error);
        }

        var @namespace = namespaceResult.Value;

        if (string.IsNullOrWhiteSpace(@namespace.ConnectionString))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Namespace connection string is not configured."));
        }

        try
        {
            var clientWrapper = _clientCache.GetOrCreate(@namespace.Id, @namespace.ConnectionString);

            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var result = await clientWrapper.SendMessageAsync(request, ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Message);
                }
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Message sent to {EntityName} in namespace {NamespaceId}",
                request.EntityName,
                request.NamespaceId);

            return Result.Success();
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Failed to send message to {EntityName} after retries",
                request.EntityName);

            return Result.Failure(Error.ExternalService(
                ErrorCodes.Message.SendFailed,
                $"Failed to send message after retries: {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.ExternalService(
                ErrorCodes.Message.SendFailed,
                ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending message");

            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while sending the message."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SendBatchAsync(IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default)
    {
        var requestList = requests.ToList();

        if (requestList.Count == 0)
        {
            return Result.Success();
        }

        var errors = new List<Error>();

        // Group by namespace and entity for efficient batch sending
        var groupedRequests = requestList.GroupBy(r => (r.NamespaceId, r.EntityName));

        foreach (var group in groupedRequests)
        {
            foreach (var request in group)
            {
                var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    errors.AddRange(result.Errors);
                }
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        return Result.Success();
    }

    private static Result ValidateRequest(SendMessageRequest request)
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

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Message.BodyRequired,
                "Message body is required."));
        }

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }
}
