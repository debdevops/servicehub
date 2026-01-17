using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Thread-safe cache for Service Bus client instances.
/// Prevents socket exhaustion by reusing clients across operations.
/// </summary>
public sealed class ServiceBusClientCache : IServiceBusClientCache
{
    private readonly ConcurrentDictionary<Guid, ServiceBusClientWrapper> _clients = new();
    private readonly ILogger<ServiceBusClientCache> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusClientCache"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    public ServiceBusClientCache(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ServiceBusClientCache>();
    }

    /// <inheritdoc/>
    public IServiceBusClientWrapper GetOrCreate(Guid namespaceId, string connectionString)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return _clients.GetOrAdd(namespaceId, id =>
        {
            _logger.LogDebug("Creating new ServiceBusClient for namespace {NamespaceId}", id);

            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpTcp,
                RetryOptions = new ServiceBusRetryOptions
                {
                    Mode = ServiceBusRetryMode.Exponential,
                    MaxRetries = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(30),
                    TryTimeout = TimeSpan.FromSeconds(60)
                }
            };

            var client = new ServiceBusClient(connectionString, clientOptions);
            var wrapperLogger = _loggerFactory.CreateLogger<ServiceBusClientWrapper>();

            return new ServiceBusClientWrapper(id, client, wrapperLogger);
        });
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(Guid namespaceId, CancellationToken cancellationToken = default)
    {
        if (_clients.TryRemove(namespaceId, out var wrapper))
        {
            _logger.LogInformation("Removing and disposing ServiceBusClient for namespace {NamespaceId}", namespaceId);

            try
            {
                await wrapper.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing ServiceBusClient for namespace {NamespaceId}", namespaceId);
            }
        }
    }

    /// <inheritdoc/>
    public bool Contains(Guid namespaceId)
    {
        return _clients.ContainsKey(namespaceId);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogInformation("Disposing ServiceBusClientCache with {Count} cached clients", _clients.Count);

        var disposeTasks = _clients.Values.Select(async wrapper =>
        {
            try
            {
                await wrapper.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing ServiceBusClient for namespace {NamespaceId}", wrapper.NamespaceId);
            }
        });

        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        _clients.Clear();
    }
}
