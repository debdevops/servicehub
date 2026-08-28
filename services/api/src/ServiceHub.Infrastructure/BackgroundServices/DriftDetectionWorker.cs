using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically runs deterministic message-shape baseline and drift
/// detection (roadmap §5.C, P1/P2 — "Baseline the good" / "Drift detection") over every active
/// namespace's DLQ feature history and caches what it finds for retrieval via
/// <c>GET /v1/drift-findings/{id}</c>.
/// </summary>
public sealed class DriftDetectionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DriftDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriftDetectionWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>DriftDetection</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public DriftDetectionWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DriftDetectionWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _detectionInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("DriftDetection:IntervalMinutes", DefaultDetectionIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("DriftDetection:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering it keep working — heartbeat recording degrades to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Drift detection worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(DriftDetectionWorker), _detectionInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during drift detection cycle");
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

        _logger.LogInformation("Drift detection worker stopping");
    }

    internal async Task RunDetectionCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var detectionService = scope.ServiceProvider.GetRequiredService<IDriftDetectionService>();
        var resultCache = scope.ServiceProvider.GetRequiredService<IDriftResultCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for drift detection: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;
        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for drift detection");
            return;
        }

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;
        var totalDetected = 0;

        foreach (var ns in namespaces)
        {
            var detectionResult = await detectionService
                .DetectDriftAsync(ns.Id, startTime, endTime, cancellationToken)
                .ConfigureAwait(false);

            if (detectionResult.IsFailure)
            {
                _logger.LogWarning(
                    "Drift detection failed for namespace {NamespaceId}: {Error}",
                    ns.Id,
                    detectionResult.Error.Message);
                continue;
            }

            if (detectionResult.Value.Count == 0)
            {
                continue;
            }

            resultCache.Store(detectionResult.Value);
            totalDetected += detectionResult.Value.Count;

            _logger.LogInformation(
                "Detected {DriftFindingCount} drift finding(s) in namespace {NamespaceId}",
                detectionResult.Value.Count,
                ns.Id);
        }

        _logger.LogDebug(
            "Drift detection cycle complete: {NamespaceCount} namespace(s) scanned, {FindingCount} finding(s) found",
            namespaces.Count,
            totalDetected);
    }
}
