using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker for detecting anomalies in message patterns.
/// This is a stub implementation for future AI-powered anomaly detection.
/// </summary>
public sealed class AnomalyDetectionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnomalyDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyDetectionWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="logger">The logger instance.</param>
    public AnomalyDetectionWorker(
        IServiceProvider serviceProvider,
        ILogger<AnomalyDetectionWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Anomaly detection worker starting");

        // Delay initial execution to allow application startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAnomaliesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during anomaly detection cycle");
            }

            try
            {
                await Task.Delay(_detectionInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("Anomaly detection worker stopping");
    }

    private async Task DetectAnomaliesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var aiServiceClient = scope.ServiceProvider.GetRequiredService<IAIServiceClient>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

        // Check if AI service is available
        var availabilityResult = await aiServiceClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (availabilityResult.IsFailure || !availabilityResult.Value)
        {
            _logger.LogDebug("AI service is not available, skipping anomaly detection cycle");
            return;
        }

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for anomaly detection: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;

        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for anomaly detection");
            return;
        }

        _logger.LogDebug(
            "Anomaly detection cycle: {NamespaceCount} active namespaces (detection not yet implemented)",
            namespaces.Count);

        // Stub: Future implementation will:
        // 1. Collect message metrics from each namespace
        // 2. Send metrics to AI service for analysis
        // 3. Store detected anomalies
        // 4. Emit alerts/notifications for critical anomalies
    }
}
