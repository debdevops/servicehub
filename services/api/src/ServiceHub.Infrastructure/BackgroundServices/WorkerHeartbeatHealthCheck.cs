using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Health check for background-worker liveness (roadmap §6, cross-cutting foundation item 4:
/// self-observability of the autonomy machinery itself) — reports, per worker, whether its
/// last recorded heartbeat is within its own configured cadence. Delivered entirely through the
/// existing ASP.NET Core health check pipeline and the existing System Health page — no new
/// schema, no new controller, no new UI.
/// </summary>
public sealed class WorkerHeartbeatHealthCheck : IHealthCheck
{
    /// <summary>
    /// The workers <c>DependencyInjection.AddBackgroundWorkers</c> registers — the "autonomy
    /// machinery" this check exists to watch over. <c>AuditService</c> and
    /// <c>InProcessPlatformEventBus</c> are lower-level event/audit plumbing, registered
    /// separately from that method, and intentionally out of scope here.
    /// </summary>
    private static readonly IReadOnlyList<string> ExpectedWorkers =
    [
        nameof(AnomalyDetectionWorker),
        nameof(DriftDetectionWorker),
        nameof(CorrelationDetectionWorker),
        nameof(ExternalSignalCorrelationWorker),
        nameof(NarrationWorker),
        nameof(BacklogForecastWorker),
        nameof(DlqMonitorWorker),
        nameof(BulkOperationWorker),
        nameof(SignatureReplayWorker),
        nameof(AuditRetentionWorker),
        nameof(RecoveryVerificationWorker),
        nameof(RecoveryAgeingWorker),
        nameof(AutonomyEvaluationWorker),
        nameof(BackupWorker),
        nameof(PlaybookExpiryWorker),
    ];

    private readonly IWorkerHeartbeatStore _store;
    private readonly WorkerHeartbeatHealthCheckOptions _options;

    /// <summary>Initializes a new instance of the <see cref="WorkerHeartbeatHealthCheck"/> class.</summary>
    public WorkerHeartbeatHealthCheck(IWorkerHeartbeatStore store, WorkerHeartbeatHealthCheckOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var heartbeats = _store.GetAll();
        var data = new Dictionary<string, object>();
        var neverReported = new List<string>();
        var stale = new List<string>();

        foreach (var workerName in ExpectedWorkers)
        {
            if (!heartbeats.TryGetValue(workerName, out var heartbeat))
            {
                neverReported.Add(workerName);
                data[workerName] = "never reported";
                continue;
            }

            var age = now - heartbeat.LastHeartbeatAtUtc;
            if (heartbeat.ExpectedInterval is { } interval)
            {
                var threshold = interval * _options.StalenessMultiplier;
                if (age > threshold)
                {
                    stale.Add(workerName);
                    data[workerName] = $"stale — last heartbeat {FormatAge(age)} ago, expected within {FormatAge(threshold)}";
                    continue;
                }
            }

            data[workerName] = $"ok — last heartbeat {FormatAge(age)} ago";
        }

        // Never Unhealthy: a stalled background worker means autonomy evaluation, DLQ
        // scanning, etc. are running behind — worth surfacing loudly, but it does not mean the
        // API itself cannot serve requests, so this check must never fail /health/ready or a
        // liveness probe and trigger a restart that would not fix a worker-level stall anyway.
        // Accordingly it is tagged "workers", not "ready" or "live" — see MapHealthCheckEndpoints.
        if (neverReported.Count > 0 || stale.Count > 0)
        {
            var parts = new List<string>();
            if (neverReported.Count > 0)
            {
                parts.Add($"{neverReported.Count} worker(s) never reported a heartbeat ({string.Join(", ", neverReported)})");
            }

            if (stale.Count > 0)
            {
                parts.Add($"{stale.Count} worker(s) stale ({string.Join(", ", stale)})");
            }

            return Task.FromResult(HealthCheckResult.Degraded(string.Join("; ", parts), data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"All {ExpectedWorkers.Count} monitored background workers reported a heartbeat within their expected cadence.",
            data));
    }

    private static string FormatAge(TimeSpan span) => span switch
    {
        { TotalHours: >= 1 } => $"{span.TotalHours:F1}h",
        { TotalMinutes: >= 1 } => $"{span.TotalMinutes:F1}m",
        _ => $"{span.TotalSeconds:F0}s",
    };
}
