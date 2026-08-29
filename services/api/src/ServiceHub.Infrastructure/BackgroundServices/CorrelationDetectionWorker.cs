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
/// Background worker that periodically runs proactive correlation (roadmap §5.D, C1 same-provider,
/// generalized by C2 cross-cloud) over every active namespace: it re-runs the same deterministic
/// anomaly detection I3 already established per namespace, then groups the results across
/// namespaces that share an owner — regardless of cloud provider — into <c>CorrelationFinding</c>s,
/// cached for retrieval via <c>GET /v1/correlation-findings/{id}</c>.
/// </summary>
public sealed class CorrelationDetectionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

    private const int DefaultDetectionIntervalMinutes = 60;
    private const int DefaultCurrentWindowHours = 24;
    private const int DefaultPushSeverityThreshold = 70;

    // PERSISTENCE-EVOLUTION-DESIGN §11 — the same significance threshold that gates a push
    // notification also gates a durable Playbook Ledger proposal; a dedicated threshold isn't
    // warranted until evidence says otherwise (§11's own "tuning decision, not architectural").
    private static readonly TimeSpan ProposalExpiry = TimeSpan.FromDays(7);
    private static readonly PlaybookActor Proposer = new("System:CorrelationDetectionWorker", PlaybookActorKind.System);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CorrelationDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

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
            "Correlation detection worker starting. Interval: {IntervalMinutes}m, current window: {WindowHours}h",
            _detectionInterval.TotalMinutes,
            _currentWindow.TotalHours);

        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(CorrelationDetectionWorker), _detectionInterval);
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
        var playbookLedger = scope.ServiceProvider.GetService<IPlaybookLedger>();

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

        var significantFindings = findings.Where(f => f.Severity >= _pushSeverityThreshold).ToList();

        if (_eventBus is not null)
        {
            foreach (var finding in significantFindings)
            {
                await PublishInsightDetectedAsync(finding, cancellationToken).ConfigureAwait(false);
            }
        }

        if (playbookLedger is not null)
        {
            foreach (var finding in significantFindings)
            {
                await ProposePlaybookEntryAsync(playbookLedger, finding, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Roadmap §5, I5 — "Push": surface findings at/above the significance threshold without
    // waiting for an operator to open DLQ Intelligence, via the existing webhook/SSE
    // infrastructure (WebhookInsightDetectedHandler / PlatformEventStreamBroker).
    private async Task PublishInsightDetectedAsync(Core.Entities.CorrelationFinding finding, CancellationToken cancellationToken)
    {
        // Owner-scoped, no NamespaceId (a correlation spans multiple namespaces) — Actor must be
        // the raw OwnerId for PlatformEventStreamBroker's visibility check to resolve it to the
        // right SSE connections, same as AutonomyEvaluationWorker's circuit-breaker event.
        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BackgroundServices.CorrelationDetectionWorker",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = finding.OwnerId,
            Payload = new InsightDetectedPayload
            {
                Kind = InsightKind.Correlation,
                FindingId = finding.Id,
                Description = finding.Description,
                Severity = finding.Severity,
                DetectedAtUtc = finding.DetectedAt,
            },
        };

        await _eventBus!.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    // PERSISTENCE-EVOLUTION-DESIGN §11 — this is the direct enabler for C4 (correlation
    // accountability): "once the Playbook Ledger exists, every C1–C3 hypothesis is logged with
    // human disposition." NamespaceId stays null — a correlation finding spans multiple
    // namespaces, same reasoning as PublishInsightDetectedAsync's owner-only Actor above.
    private async Task ProposePlaybookEntryAsync(
        IPlaybookLedger playbookLedger, Core.Entities.CorrelationFinding finding, CancellationToken cancellationToken)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            Providers = finding.Providers.Select(p => p.ToString()),
            Members = finding.Members.Select(m => new
            {
                m.NamespaceId,
                m.EntityName,
                AnomalyType = m.AnomalyType.ToString(),
                m.Severity,
                Provider = m.Provider.ToString(),
            }),
            finding.Severity,
            finding.Description,
            finding.RecommendedActions,
        });
        var evidenceRefJson = JsonSerializer.Serialize(new { CorrelationFindingId = finding.Id, finding.DetectedAt });

        var result = await playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = finding.OwnerId,
            PillarKind = PillarKind.Correlate,
            ProposalKind = "CorrelationHypothesis",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = Proposer,
            ExpiresAfter = ProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose Playbook Ledger entry for correlation finding {CorrelationFindingId}: {Error}",
                finding.Id,
                result.Error.Message);
        }
    }
}
