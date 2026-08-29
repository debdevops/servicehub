using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically runs deterministic backlog-growth forecasting (roadmap
/// §5.E, P4 — "Predictive backlog signal") over every active namespace's DLQ history and caches
/// what it finds for retrieval via <c>GET /v1/backlog-forecasts/{id}</c>.
/// </summary>
public sealed class BacklogForecastWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    private const int DefaultForecastIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;
    private const int DefaultPushSeverityThreshold = 70;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BacklogForecastWorker> _logger;
    private readonly TimeSpan _forecastInterval;
    private readonly TimeSpan _currentWindow;
    private readonly int _alertThreshold;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

    /// <summary>
    /// Initializes a new instance of the <see cref="BacklogForecastWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>BacklogForecast</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public BacklogForecastWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<BacklogForecastWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _forecastInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("BacklogForecast:IntervalMinutes", DefaultForecastIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("BacklogForecast:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));
        _alertThreshold = Math.Max(
            1,
            configuration.GetValue("BacklogForecast:AlertThreshold", DeterministicBacklogForecastService.DefaultAlertThreshold));
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
            "Backlog forecast worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h, threshold: {Threshold}",
            _forecastInterval.TotalMinutes,
            _currentWindow.TotalHours,
            _alertThreshold);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunForecastCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(BacklogForecastWorker), _forecastInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during backlog forecast cycle");
            }

            try
            {
                await Task.Delay(_forecastInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Backlog forecast worker stopping");
    }

    internal async Task RunForecastCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var forecastService = scope.ServiceProvider.GetRequiredService<IBacklogForecastService>();
        var resultCache = scope.ServiceProvider.GetRequiredService<IBacklogForecastResultCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for backlog forecasting: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;
        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for backlog forecasting");
            return;
        }

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;
        var totalForecast = 0;

        foreach (var ns in namespaces)
        {
            var forecastResult = await forecastService
                .ForecastAsync(ns.Id, startTime, endTime, _alertThreshold, cancellationToken)
                .ConfigureAwait(false);

            if (forecastResult.IsFailure)
            {
                _logger.LogWarning(
                    "Backlog forecasting failed for namespace {NamespaceId}: {Error}",
                    ns.Id,
                    forecastResult.Error.Message);
                continue;
            }

            if (forecastResult.Value.Count == 0)
            {
                continue;
            }

            resultCache.Store(forecastResult.Value);
            totalForecast += forecastResult.Value.Count;

            _logger.LogInformation(
                "Projected {ForecastCount} backlog breach(es) in namespace {NamespaceId}",
                forecastResult.Value.Count,
                ns.Id);

            if (_eventBus is not null)
            {
                foreach (var forecast in forecastResult.Value.Where(f => f.Severity >= _pushSeverityThreshold))
                {
                    await PublishInsightDetectedAsync(forecast, ns, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug(
            "Backlog forecast cycle complete: {NamespaceCount} namespace(s) scanned, {ForecastCount} projection(s) found",
            namespaces.Count,
            totalForecast);
    }

    // Roadmap §5, I5 — "Push": surface projected breaches at/above the significance threshold
    // without waiting for an operator to open DLQ Intelligence, via the existing webhook/SSE
    // infrastructure (WebhookInsightDetectedHandler / PlatformEventStreamBroker).
    private async Task PublishInsightDetectedAsync(
        Core.Entities.BacklogForecast forecast,
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BackgroundServices.BacklogForecastWorker",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = ns.OwnerId,
            NamespaceId = ns.Id,
            NamespaceName = ns.Name,
            TargetScope = forecast.EntityName,
            Payload = new InsightDetectedPayload
            {
                Kind = InsightKind.BacklogForecast,
                FindingId = forecast.Id,
                EntityName = forecast.EntityName,
                Description = forecast.Description,
                Severity = forecast.Severity,
                DetectedAtUtc = forecast.DetectedAt,
            },
        };

        await _eventBus!.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }
}
