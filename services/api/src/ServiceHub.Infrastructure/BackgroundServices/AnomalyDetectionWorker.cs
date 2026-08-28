using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically runs deterministic, statistics-based anomaly detection
/// (roadmap §5.B, I3 — "Anomalize") over every active namespace's DLQ history and caches what it
/// finds for retrieval via <c>GET /v1/anomalies/{id}</c>.
/// </summary>
public sealed class AnomalyDetectionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;
    private const int DefaultPushSeverityThreshold = 70;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnomalyDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyDetectionWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>AnomalyDetection</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public AnomalyDetectionWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AnomalyDetectionWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _detectionInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("AnomalyDetection:IntervalMinutes", DefaultDetectionIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("AnomalyDetection:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));
        _pushSeverityThreshold = Math.Clamp(
            configuration.GetValue("Insight:PushSeverityThreshold", DefaultPushSeverityThreshold),
            0, 100);

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering these keep working — heartbeat recording and push notification
        // (roadmap §5, I5) degrade to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
        _eventBus = serviceProvider.GetService<IPlatformEventBus>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Anomaly detection worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(AnomalyDetectionWorker), _detectionInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
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
                break;
            }
        }

        _logger.LogInformation("Anomaly detection worker stopping");
    }

    internal async Task RunDetectionCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var detectionService = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
        var resultCache = scope.ServiceProvider.GetRequiredService<IAnomalyResultCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

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

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;
        var totalDetected = 0;

        foreach (var ns in namespaces)
        {
            var detectionResult = await detectionService
                .DetectAnomaliesAsync(ns.Id, startTime, endTime, cancellationToken)
                .ConfigureAwait(false);

            if (detectionResult.IsFailure)
            {
                _logger.LogWarning(
                    "Anomaly detection failed for namespace {NamespaceId}: {Error}",
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
                "Detected {AnomalyCount} anomaly(ies) in namespace {NamespaceId}",
                detectionResult.Value.Count,
                ns.Id);

            if (_eventBus is not null)
            {
                foreach (var anomaly in detectionResult.Value.Where(a => a.Severity >= _pushSeverityThreshold))
                {
                    await PublishInsightDetectedAsync(anomaly, ns, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug(
            "Anomaly detection cycle complete: {NamespaceCount} namespace(s) scanned, {AnomalyCount} anomaly(ies) found",
            namespaces.Count,
            totalDetected);
    }

    // Roadmap §5, I5 — "Push": surface findings at/above the significance threshold without
    // waiting for an operator to open DLQ Intelligence, via the existing webhook/SSE
    // infrastructure (WebhookInsightDetectedHandler / PlatformEventStreamBroker).
    private async Task PublishInsightDetectedAsync(
        Core.Entities.Anomaly anomaly,
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BackgroundServices.AnomalyDetectionWorker",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = ns.OwnerId,
            NamespaceId = ns.Id,
            NamespaceName = ns.Name,
            TargetScope = anomaly.EntityName,
            Payload = new InsightDetectedPayload
            {
                Kind = InsightKind.Anomaly,
                FindingId = anomaly.Id,
                EntityName = anomaly.EntityName,
                Description = anomaly.Description,
                Severity = anomaly.Severity,
                DetectedAtUtc = anomaly.DetectedAt,
            },
        };

        await _eventBus!.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }
}
