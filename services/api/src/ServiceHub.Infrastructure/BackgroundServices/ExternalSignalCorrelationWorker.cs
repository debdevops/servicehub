using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically correlates anomaly onset against recorded external signals
/// (roadmap §5.D, C3 — external-signal correlation; M5, ADR-0008). Completes C3's proactive-detection
/// story the same way <see cref="CorrelationDetectionWorker"/> (C1/C2) already does: unprompted
/// detection, cached for <c>GET /v1/external-signals/correlations/{id}</c>, pushed above the shared
/// significance threshold, and proposed into the Playbook Ledger as an ordinary
/// <c>CorrelationHypothesis</c> — the same <c>ProposalKind</c> C1/C2 use, per
/// <c>PERSISTENCE-EVOLUTION-DESIGN-2026-08-29.md</c> §12: "the hypothesis that results from
/// correlating against a stored signal is still an ordinary Playbook proposal." This is what makes
/// C4 (correlation accountability) — which already reads <c>PillarKind.Correlate</c> +
/// <c>CorrelationHypothesis</c> generically — pick up C3's findings with zero changes of its own.
/// </summary>
public sealed class ExternalSignalCorrelationWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(75);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;
    private const int DefaultCorrelationWindowHours = 6;
    private const int MaxCorrelationWindowHours = 168;
    private const int DefaultPushSeverityThreshold = 70;
    private const int SignalQueryLimit = 500;

    // PERSISTENCE-EVOLUTION-DESIGN §11 — the same significance threshold that gates a push
    // notification also gates a durable Playbook Ledger proposal; a dedicated threshold isn't
    // warranted until evidence says otherwise (§11's own "tuning decision, not architectural").
    private static readonly TimeSpan ProposalExpiry = TimeSpan.FromDays(7);
    private static readonly PlaybookActor Proposer = new("System:ExternalSignalCorrelationWorker", PlaybookActorKind.System);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExternalSignalCorrelationWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly TimeSpan _correlationWindow;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSignalCorrelationWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>ExternalSignalCorrelation</c> section).</param>
    /// <param name="logger">The logger instance.</param>
    public ExternalSignalCorrelationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ExternalSignalCorrelationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _detectionInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("ExternalSignalCorrelation:IntervalMinutes", DefaultDetectionIntervalMinutes),
            1, 1440));
        _currentWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("ExternalSignalCorrelation:CurrentWindowHours", DefaultCurrentWindowHours),
            1, 168));
        _correlationWindow = TimeSpan.FromHours(Math.Clamp(
            configuration.GetValue("ExternalSignalCorrelation:CorrelationWindowHours", DefaultCorrelationWindowHours),
            1, MaxCorrelationWindowHours));
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
            "External-signal correlation worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h, correlation window: {CorrelationWindowHours}h",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours,
            _correlationWindow.TotalHours);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(ExternalSignalCorrelationWorker), _detectionInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during external-signal correlation cycle");
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

        _logger.LogInformation("External-signal correlation worker stopping");
    }

    internal async Task RunDetectionCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var anomalyDetectionService = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
        var correlationService = scope.ServiceProvider.GetRequiredService<IExternalSignalCorrelationService>();
        var externalSignalRepository = scope.ServiceProvider.GetRequiredService<IExternalSignalRepository>();
        var resultCache = scope.ServiceProvider.GetRequiredService<IExternalSignalCorrelationCache>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
        var playbookLedger = scope.ServiceProvider.GetService<IPlaybookLedger>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for external-signal correlation: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var namespaces = namespacesResult.Value;
        if (namespaces.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for external-signal correlation");
            return;
        }

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _currentWindow;
        var totalCorrelated = 0;

        // Owners are correlated independently: an anomaly can only ever be correlated against its
        // own owner's signals (DeterministicExternalSignalCorrelationService groups by OwnerId
        // internally too), so batching per owner rather than across the whole fleet at once keeps
        // this identical in behavior to the on-demand controller path for a single-owner caller.
        foreach (var ownerGroup in namespaces.GroupBy(n => n.OwnerId))
        {
            var ownerId = ownerGroup.Key;
            var ownerNamespaces = ownerGroup.ToDictionary(n => n.Id);

            var observations = new List<AnomalyObservation>();
            foreach (var ns in ownerNamespaces.Values)
            {
                var detectionResult = await anomalyDetectionService
                    .DetectAnomaliesAsync(ns.Id, startTime, endTime, cancellationToken)
                    .ConfigureAwait(false);

                if (detectionResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Anomaly detection failed for namespace {NamespaceId} during external-signal correlation: {Error}",
                        ns.Id,
                        detectionResult.Error.Message);
                    continue;
                }

                observations.AddRange(detectionResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
            }

            if (observations.Count == 0)
            {
                continue;
            }

            // Signals may have occurred before startTime and still be within the correlation
            // window of an anomaly detected at the very start of it, so the signal lookback starts
            // one window earlier than the anomaly-analysis window itself (mirrors
            // ExternalSignalsController.DetectCorrelations).
            var signals = await externalSignalRepository.QueryAsync(
                ownerId, namespaceId: null, startTime - _correlationWindow, endTime, SignalQueryLimit, cancellationToken)
                .ConfigureAwait(false);

            if (signals.Count == 0)
            {
                continue;
            }

            var correlations = correlationService.DetectCorrelations(observations, signals, _correlationWindow);
            if (correlations.Count == 0)
            {
                continue;
            }

            resultCache.Store(correlations);
            totalCorrelated += correlations.Count;

            _logger.LogInformation(
                "Detected {CorrelationCount} external-signal correlation(s) for owner {OwnerId}",
                correlations.Count,
                ownerId);

            var significantCorrelations = correlations.Where(c => c.AnomalySeverity >= _pushSeverityThreshold).ToList();

            foreach (var correlation in significantCorrelations)
            {
                ownerNamespaces.TryGetValue(correlation.NamespaceId, out var ns);

                if (_eventBus is not null)
                {
                    await PublishInsightDetectedAsync(correlation, ns, cancellationToken).ConfigureAwait(false);
                }

                if (playbookLedger is not null)
                {
                    await ProposePlaybookEntryAsync(playbookLedger, correlation, ns, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug(
            "External-signal correlation cycle complete: {NamespaceCount} namespace(s) scanned, {CorrelationCount} correlation(s) found",
            namespaces.Count,
            totalCorrelated);
    }

    // Roadmap §5, I5 — "Push": surface findings at/above the significance threshold without
    // waiting for an operator to open DLQ Intelligence, via the existing webhook/SSE
    // infrastructure (WebhookInsightDetectedHandler / PlatformEventStreamBroker).
    private async Task PublishInsightDetectedAsync(
        Core.Entities.ExternalSignalCorrelation correlation, Core.Entities.Namespace? ns, CancellationToken cancellationToken)
    {
        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BackgroundServices.ExternalSignalCorrelationWorker",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = correlation.OwnerId,
            NamespaceId = correlation.NamespaceId,
            NamespaceName = ns?.Name,
            TargetScope = correlation.EntityName,
            Payload = new InsightDetectedPayload
            {
                Kind = InsightKind.ExternalSignalCorrelation,
                FindingId = correlation.Id,
                EntityName = correlation.EntityName,
                Description = correlation.Description,
                Severity = correlation.AnomalySeverity,
                DetectedAtUtc = correlation.DetectedAt,
            },
        };

        await _eventBus!.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    // PERSISTENCE-EVOLUTION-DESIGN §12 — "the hypothesis that results from correlating against a
    // stored signal is still an ordinary Playbook proposal": same ProposalKind C1/C2 use, so C4
    // (correlation accountability) picks this up with zero changes of its own.
    private async Task ProposePlaybookEntryAsync(
        IPlaybookLedger playbookLedger,
        Core.Entities.ExternalSignalCorrelation correlation,
        Core.Entities.Namespace? ns,
        CancellationToken cancellationToken)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            correlation.EntityName,
            AnomalyType = correlation.AnomalyType.ToString(),
            correlation.AnomalySeverity,
            Provider = correlation.Provider.ToString(),
            SignalType = correlation.SignalType.ToString(),
            correlation.SignalSource,
            correlation.SignalOccurredAt,
            GapMinutes = correlation.Gap.TotalMinutes,
            correlation.Description,
            correlation.RecommendedActions,
        });
        var evidenceRefJson = JsonSerializer.Serialize(new
        {
            ExternalSignalCorrelationId = correlation.Id,
            correlation.SignalId,
            correlation.DetectedAt,
        });

        var result = await playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = correlation.OwnerId,
            PillarKind = PillarKind.Correlate,
            ProposalKind = "CorrelationHypothesis",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = Proposer,
            NamespaceId = correlation.NamespaceId,
            NamespaceNameSnapshot = ns?.Name,
            ProviderSnapshot = correlation.Provider,
            EnvironmentSnapshot = ns?.Environment,
            ExpiresAfter = ProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose Playbook Ledger entry for external-signal correlation {ExternalSignalCorrelationId}: {Error}",
                correlation.Id,
                result.Error.Message);
        }
    }
}
