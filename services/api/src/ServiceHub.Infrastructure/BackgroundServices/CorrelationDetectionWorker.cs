using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically runs same-provider proactive correlation (roadmap §5.D,
/// C1) over every active namespace: it re-runs the same deterministic anomaly detection I3
/// already established per namespace, then groups the results across namespaces that share an
/// owner and cloud provider into <c>CorrelationFinding</c>s, cached for retrieval via
/// <c>GET /v1/correlation-findings/{id}</c>.
/// </summary>
public sealed class CorrelationDetectionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CorrelationDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationDetectionWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>CorrelationDetection</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public CorrelationDetectionWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<CorrelationDetectionWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _detectionInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("CorrelationDetection:IntervalMinutes", DefaultDetectionIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("CorrelationDetection:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Correlation detection worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during correlation detection cycle");
            }

            try
            {
                await Task.Delay(_detectionInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Correlation detection worker stopping");
    }

    internal async Task RunDetectionCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var anomalyDetectionService = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
        var correlationDetectionService = scope.ServiceProvider.GetRequiredService<ICorrelationDetectionService>();
        var resultCache = scope.ServiceProvider.GetRequiredService<ICorrelationResultCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for correlation detection: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;
        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for correlation detection");
            return;
        }

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;
        var observations = new List<AnomalyObservation>();

        foreach (var ns in namespaces)
        {
            var detectionResult = await anomalyDetectionService
                .DetectAnomaliesAsync(ns.Id, startTime, endTime, cancellationToken)
                .ConfigureAwait(false);

            if (detectionResult.IsFailure)
            {
                _logger.LogWarning(
                    "Anomaly detection failed for namespace {NamespaceId} during correlation cycle: {Error}",
                    ns.Id,
                    detectionResult.Error.Message);
                continue;
            }

            observations.AddRange(detectionResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
        }

        if (observations.Count == 0)
        {
            _logger.LogDebug("Correlation detection cycle complete: no anomalies to correlate");
            return;
        }

        var findings = correlationDetectionService.DetectCorrelations(observations);
        if (findings.Count == 0)
        {
            _logger.LogDebug(
                "Correlation detection cycle complete: {ObservationCount} anomaly(ies) observed, no correlations",
                observations.Count);
            return;
        }

        resultCache.Store(findings);

        _logger.LogInformation(
            "Correlation detection cycle complete: {NamespaceCount} namespace(s) scanned, {FindingCount} correlation(s) found",
            namespaces.Count,
            findings.Count);
    }
}
