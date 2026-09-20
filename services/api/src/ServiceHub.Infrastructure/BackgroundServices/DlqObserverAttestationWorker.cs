using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Drives the infrastructure-attested DLQ observer's liveness canary (`cloud-platform-infra`
/// ADR-004 item 4; ADR-0011) — the only mechanism that may ever turn a namespace's
/// <c>CanProveDlqAbsence</c> override on. Every sweep cycle, for each namespace with attestation
/// <see cref="Core.Entities.DlqObserverAttestation.Enabled"/>: first checks whether the
/// <em>previous</em> cycle's canary was recorded by the observer's log (confirming it, if so), then
/// dispatches a fresh canary for the <em>next</em> cycle to check. Confirmation is never assumed —
/// a namespace whose canary the observer's log cannot corroborate simply stops receiving fresh
/// confirmations, and <see cref="Core.Entities.DlqObserverAttestation.IsLiveAt"/> ages past its
/// staleness bound on its own, entirely by this worker's absence of a positive write.
/// </summary>
public sealed class DlqObserverAttestationWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    private const int DefaultSweepIntervalSeconds = 300;
    private const string CanaryApplicationPropertyKey = "x-servicehub-observer-canary-id";
    private const string CanaryBody = "servicehub-dlq-observer-liveness-canary";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DlqObserverAttestationWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="DlqObserverAttestationWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>DlqObserverAttestation:SweepIntervalSeconds</c>
    /// (default 300, clamped to [60, 3600]). Also the canary cadence: one canary is dispatched per
    /// sweep per enabled namespace.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public DlqObserverAttestationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DlqObserverAttestationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DlqObserverAttestation:SweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 3600));

        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DLQ Observer Attestation Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var attestationService = scope.ServiceProvider.GetRequiredService<IDlqObserverAttestationService>();

                var enabled = await attestationService.GetAllEnabledAsync(stoppingToken);
                foreach (var attestation in enabled)
                {
                    await SweepNamespaceAsync(scope.ServiceProvider, attestation, stoppingToken);
                }

                _heartbeatStore?.RecordHeartbeat(nameof(DlqObserverAttestationWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DLQ Observer Attestation Worker sweep cycle");
            }

            try
            {
                await Task.Delay(_sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DLQ Observer Attestation Worker stopped");
    }

    /// <summary>Sweeps one namespace's attestation: checks the previous canary, dispatches the
    /// next. Internal so tests can drive a single sweep cycle directly instead of waiting on
    /// <see cref="ExecuteAsync"/>'s timer loop.</summary>
    internal async Task SweepNamespaceAsync(
        IServiceProvider services, Core.Entities.DlqObserverAttestation attestation, CancellationToken cancellationToken)
    {
        var attestationService = services.GetRequiredService<IDlqObserverAttestationService>();

        if (string.IsNullOrWhiteSpace(attestation.ObserverReference) || string.IsNullOrWhiteSpace(attestation.DlqEntityName))
        {
            _logger.LogWarning(
                "DLQ observer attestation for namespace {NamespaceId} is enabled but missing ObserverReference or DlqEntityName — skipping",
                attestation.NamespaceId);
            return;
        }

        var namespaceRepo = services.GetRequiredService<INamespaceRepository>();
        var namespaceResult = await namespaceRepo.GetByIdAsync(attestation.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            _logger.LogWarning(
                "DLQ observer attestation for namespace {NamespaceId} could not resolve the namespace: {Error} — skipping",
                attestation.NamespaceId, namespaceResult.Error.Message);
            return;
        }

        var ns = namespaceResult.Value;

        // ── Step 1: check the previous cycle's canary ──────────────────────
        if (!string.IsNullOrEmpty(attestation.LastCanaryMessageId))
        {
            var readers = services.GetServices<IDlqObserverLogReader>();
            var reader = readers.FirstOrDefault(r => r.Provider == ns.Provider);
            if (reader is null)
            {
                _logger.LogWarning(
                    "No DLQ observer log reader registered for provider {Provider} (namespace {NamespaceId}) — cannot confirm canary",
                    ns.Provider, ns.Id);
            }
            else
            {
                bool confirmed;
                try
                {
                    confirmed = await reader.HasRecordedArrivalAsync(
                        ns, attestation.ObserverReference, attestation.LastCanaryMessageId, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Fail closed: an unrunnable liveness check must never be silently treated as
                    // confirmed (ADR-004 item 4's "never assume fine").
                    _logger.LogWarning(ex,
                        "DLQ observer log read failed for namespace {NamespaceId} — treating canary as unconfirmed this cycle",
                        ns.Id);
                    confirmed = false;
                }

                if (confirmed)
                {
                    await attestationService.RecordCanaryConfirmedAsync(attestation.OwnerId, attestation.NamespaceId, cancellationToken);
                }
            }
        }

        // ── Step 2: dispatch the next cycle's canary ────────────────────────
        var canaryId = Guid.NewGuid().ToString("N");
        var messageOperations = services.GetRequiredService<IMessageOperationsService>();
        var sendResult = await messageOperations.SendAsync(new SendMessageRequest(
            NamespaceId: attestation.NamespaceId,
            EntityName: attestation.DlqEntityName,
            Body: CanaryBody,
            ApplicationProperties: new Dictionary<string, object> { [CanaryApplicationPropertyKey] = canaryId }),
            cancellationToken);

        if (sendResult.IsFailure)
        {
            _logger.LogWarning(
                "DLQ observer liveness canary send failed for namespace {NamespaceId}: {Error} — no new canary recorded this cycle",
                ns.Id, sendResult.Error.Message);
            return;
        }

        await attestationService.RecordCanarySentAsync(attestation.OwnerId, attestation.NamespaceId, canaryId, cancellationToken);
    }
}
