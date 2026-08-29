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
    private const int DefaultPushSeverityThreshold = 70;

    // PERSISTENCE-EVOLUTION-DESIGN §11 — the same significance threshold that gates a push
    // notification also gates a durable Playbook Ledger proposal; a dedicated threshold isn't
    // warranted until evidence says otherwise (§11's own "tuning decision, not architectural").
    private static readonly TimeSpan ProposalExpiry = TimeSpan.FromDays(7);
    private static readonly PlaybookActor Proposer = new("System:DriftDetectionWorker", PlaybookActorKind.System);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DriftDetectionWorker> _logger;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _currentWindow;
    private readonly int _pushSeverityThreshold;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;
    private readonly IPlatformEventBus? _eventBus;

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
        var playbookLedger = scope.ServiceProvider.GetService<IPlaybookLedger>();

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

            var significantFindings = detectionResult.Value
                .Where(f => f.Severity >= _pushSeverityThreshold)
                .ToList();

            if (_eventBus is not null)
            {
                foreach (var finding in significantFindings)
                {
                    await PublishInsightDetectedAsync(finding, ns, cancellationToken).ConfigureAwait(false);
                }
            }

            if (playbookLedger is not null)
            {
                foreach (var finding in significantFindings)
                {
                    await ProposePlaybookEntryAsync(playbookLedger, finding, ns, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug(
            "Drift detection cycle complete: {NamespaceCount} namespace(s) scanned, {FindingCount} finding(s) found",
            namespaces.Count,
            totalDetected);
    }

    // Roadmap §5, I5 — "Push": surface findings at/above the significance threshold without
    // waiting for an operator to open DLQ Intelligence, via the existing webhook/SSE
    // infrastructure (WebhookInsightDetectedHandler / PlatformEventStreamBroker).
    private async Task PublishInsightDetectedAsync(
        Core.Entities.DriftFinding finding,
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        var evt = new PlatformEvent
        {
            Source = "ServiceHub.Infrastructure.BackgroundServices.DriftDetectionWorker",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = ns.OwnerId,
            NamespaceId = ns.Id,
            NamespaceName = ns.Name,
            TargetScope = finding.EntityName,
            Payload = new InsightDetectedPayload
            {
                Kind = InsightKind.Drift,
                FindingId = finding.Id,
                EntityName = finding.EntityName,
                Description = finding.Description,
                Severity = finding.Severity,
                DetectedAtUtc = finding.DetectedAt,
            },
        };

        await _eventBus!.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    // PERSISTENCE-EVOLUTION-DESIGN §11 — the direct enabler for C4 (correlation accountability)
    // and a contributor to P5 (prevention-rule backtesting): a finding at/above the significance
    // threshold gets a durable, human-dispositioned Playbook Ledger entry, not just an ephemeral
    // cache row. Never itself a trigger — a human still decides whether the drift matters.
    private async Task ProposePlaybookEntryAsync(
        IPlaybookLedger playbookLedger,
        Core.Entities.DriftFinding finding,
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            finding.EntityName,
            Type = finding.Type.ToString(),
            finding.Severity,
            finding.Description,
            finding.RecommendedActions,
        });
        var evidenceRefJson = JsonSerializer.Serialize(new { DriftFindingId = finding.Id, finding.DetectedAt });

        var result = await playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = ns.OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "DriftFinding",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = Proposer,
            NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            ExpiresAfter = ProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose Playbook Ledger entry for drift finding {DriftFindingId} in namespace {NamespaceId}: {Error}",
                finding.Id,
                ns.Id,
                result.Error.Message);
        }
    }
}
