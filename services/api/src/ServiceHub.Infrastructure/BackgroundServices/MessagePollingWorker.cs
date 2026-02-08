using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker for polling Service Bus messages.
/// This is a stub implementation for future real-time message monitoring.
/// </summary>
public sealed class MessagePollingWorker : BackgroundService
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IMessageReceiver _messageReceiver;
    private readonly ILogger<MessagePollingWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePollingWorker"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="messageReceiver">The message receiver.</param>
    /// <param name="logger">The logger instance.</param>
    public MessagePollingWorker(
        INamespaceRepository namespaceRepository,
        IMessageReceiver messageReceiver,
        ILogger<MessagePollingWorker> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Message polling worker starting");

        // Delay initial execution to allow application startup
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollMessagesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message polling cycle");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("Message polling worker stopping");
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        var namespacesResult = await _namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for polling: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;

        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for message polling");
            return;
        }

        _logger.LogDebug(
            "Message polling cycle: {NamespaceCount} active namespaces (polling not yet implemented)",
            namespaces.Count);

        // Stub: Future implementation will poll messages from configured queues/subscriptions
        // and emit events or update a message cache for real-time UI updates
    }
}
