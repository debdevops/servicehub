using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically stitches I1–I3's structured findings together with P1/P2
/// drift and C1 correlation output into deterministic, plain-English narrations (roadmap §5.B,
/// I4 — "Narrate") and, for narrations at or above its own significance threshold, pushes them
/// via the webhook/SSE infrastructure without waiting for an operator to open the app (roadmap
/// §5, I5 — "Push").
/// <para>
/// Recomputes anomalies, drift findings, and correlations directly from
/// <see cref="IAnomalyDetectionService"/>/<see cref="IDriftDetectionService"/>/
/// <see cref="ICorrelationDetectionService"/> rather than reading the other workers' caches —
/// the same "recompute, don't share a cache" pattern <see cref="CorrelationDetectionWorker"/>
/// already uses for its own anomaly recomputation, since these are cheap deterministic stats over
/// already-stored DLQ data, not an expensive external call.
/// </para>
/// </summary>
public sealed class NarrationWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(75);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;
    private const int DefaultPushSeverityThreshold = 70;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NarrationWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

    /// <summary>
    /// Initializes a new instance of the <see cref="NarrationWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>Narration</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public NarrationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<NarrationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _detectionInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("Narration:IntervalMinutes", DefaultDetectionIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("Narration:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));
        _pushSeverityThreshold = Math.Clamp(
            configuration.GetValue("Insight:PushSeverityThreshold", DefaultPushSeverityThreshold),
            0, 100);

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering these keep working — heartbeat recording and push notification
        // degrade to a no-op instead, matching the pattern already established by the other
        // detection workers' IWorkerHeartbeatStore resolution.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
        _eventBus = serviceProvider.GetService<IPlatformEventBus>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Narration worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h, push threshold: {Threshold}",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours,
            _pushSeverityThreshold);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunNarrationCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(NarrationWorker), _detectionInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during narration cycle");
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

        _logger.LogInformation("Narration worker stopping");
    }

    internal async Task RunNarrationCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var anomalyDetectionService = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
        var driftDetectionService = scope.ServiceProvider.GetRequiredService<IDriftDetectionService>();
        var correlationDetectionService = scope.ServiceProvider.GetRequiredService<ICorrelationDetectionService>();
        var narrationService = scope.ServiceProvider.GetRequiredService<INarrationService>();
        var resultCache = scope.ServiceProvider.GetRequiredService<INarrationResultCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for narration: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;
        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for narration");
            return;
        }

        var namespacesById = namespaces.ToDictionary(n => n.Id);
        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;

        var anomalies = new List<Anomaly>();
        var driftFindings = new List<DriftFinding>();
        var observations = new List<AnomalyObservation>();

        foreach (var ns in namespaces)
        {
            var anomalyResult = await anomalyDetectionService
                .DetectAnomaliesAsync(ns.Id, startTime, endTime, cancellationToken)
                .ConfigureAwait(false);

            if (anomalyResult.IsFailure)
            {
                _logger.LogWarning(
                    "Anomaly detection failed for namespace {NamespaceId} during narration cycle: {Error}",
                    ns.Id,
                    anomalyResult.Error.Message);
            }
            else
            {
                anomalies.AddRange(anomalyResult.Value);
                observations.AddRange(anomalyResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
            }

            var driftResult = await driftDetectionService
                .DetectDriftAsync(ns.Id, startTime, endTime, cancellationToken)
                .ConfigureAwait(false);

            if (driftResult.IsFailure)
            {
                _logger.LogWarning(
                    "Drift detection failed for namespace {NamespaceId} during narration cycle: {Error}",
                    ns.Id,
                    driftResult.Error.Message);
            }
            else
            {
                driftFindings.AddRange(driftResult.Value);
            }
        }

        var correlationFindings = correlationDetectionService.DetectCorrelations(observations);

        var narrations = narrationService.GenerateNarrations(namespacesById, anomalies, driftFindings, correlationFindings);
        if (narrations.Count == 0)
        {
            _logger.LogDebug("Narration cycle complete: nothing to narrate");
            return;
        }

        resultCache.Store(narrations);

        _logger.LogInformation(
            "Narration cycle complete: {NamespaceCount} namespace(s) scanned, {NarrationCount} narration(s) generated",
            namespaces.Count,
            narrations.Count);

        if (_eventBus is null)
        {
            return;
        }

        var correlationFindingsById = correlationFindings.ToDictionary(f => f.Id);

        foreach (var narration in narrations.Where(n => n.Severity >= _pushSeverityThreshold))
        {
            var (ownerId, namespaceId, namespaceName) = ResolvePushContext(narration, namespacesById, correlationFindingsById);
            if (ownerId is null)
            {
                continue;
            }

            var evt = new PlatformEvent
            {
                Source = "ServiceHub.Infrastructure.BackgroundServices.NarrationWorker",
                Category = EventCategories.Insight,
                EventType = EventTypes.InsightDetected,
                Severity = EventSeverity.Warning,
                Actor = ownerId,
                NamespaceId = namespaceId,
                NamespaceName = namespaceName,
                Payload = new InsightDetectedPayload
                {
                    Kind = InsightKind.Narration,
                    FindingId = narration.Id,
                    Description = narration.Summary,
                    Severity = narration.Severity,
                    DetectedAtUtc = narration.GeneratedAt,
                },
            };

            await _eventBus.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
        }
    }

    private static (string? OwnerId, Guid? NamespaceId, string? NamespaceName) ResolvePushContext(
        Narration narration,
        IReadOnlyDictionary<Guid, Namespace> namespacesById,
        IReadOnlyDictionary<Guid, CorrelationFinding> correlationFindingsById)
    {
        if (narration.Kind == NarrationKind.NamespaceActivity
            && narration.NamespaceId is Guid namespaceId
            && namespacesById.TryGetValue(namespaceId, out var ns))
        {
            return (ns.OwnerId, ns.Id, ns.Name);
        }

        if (narration.Kind == NarrationKind.CrossNamespaceCorrelation
            && narration.ContributingCorrelationFindingIds.Count > 0
            && correlationFindingsById.TryGetValue(narration.ContributingCorrelationFindingIds[0], out var correlation))
        {
            // Owner-scoped, no NamespaceId (a correlation spans multiple namespaces) — Actor must
            // be the raw OwnerId for PlatformEventStreamBroker's visibility check to resolve it to
            // the right SSE connections, same as AutonomyEvaluationWorker's circuit-breaker event.
            return (correlation.OwnerId, null, null);
        }

        return (null, null, null);
    }
}
