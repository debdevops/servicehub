using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Drains <see cref="IBulkOperationQueue"/> and processes bulk replay/purge jobs one at a time
/// via a fresh <see cref="IBulkOperationExecutor"/> scope per job.
/// </summary>
/// <remarks>
/// Single concurrency across jobs is deliberate for this first version: bulk operations are
/// infrequent, human-triggered actions (not a high-throughput pipeline), and processing more
/// than one job at a time would mean two jobs could race against provider rate limits or the
/// same entity simultaneously with no coordination. If real usage shows jobs queueing up behind
/// each other, bounded cross-job concurrency is a contained follow-up — see
/// docs/EXTENDING-PROVIDERS.md's note on avoiding speculative concurrency.
/// </remarks>
public sealed class BulkOperationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBulkOperationQueue _queue;
    private readonly ILogger<BulkOperationWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    /// <summary>Initializes a new instance of the <see cref="BulkOperationWorker"/> class.</summary>
    public BulkOperationWorker(
        IServiceProvider serviceProvider,
        IBulkOperationQueue queue,
        ILogger<BulkOperationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering it keep working — heartbeat recording degrades to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

        // Queue-driven, not timer-driven: no expected cadence between jobs, so no
        // staleness check applies once this proves the worker actually started and entered
        // its dequeue loop (see IWorkerHeartbeatStore's docs on event-driven workers).
        _heartbeatStore?.RecordHeartbeat(nameof(BulkOperationWorker), expectedInterval: null);

        await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
                _heartbeatStore?.RecordHeartbeat(nameof(BulkOperationWorker), expectedInterval: null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — let the outer loop exit.
                break;
            }
            catch (Exception ex)
            {
                // A job-level failure is already recorded on the job row by the executor's own
                // try/catch; this outer catch only guards against the executor itself throwing
                // before it can record that (e.g. DI resolution failure) so one bad job can
                // never take the whole worker down.
                _logger.LogError(ex, "Unhandled error processing bulk operation job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, _queue.RegisterRunning(jobId));

        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IBulkOperationExecutor>();

        try
        {
            await executor.ExecuteAsync(jobId, jobCts.Token);
        }
        finally
        {
            _queue.Complete(jobId);
        }
    }

    /// <summary>
    /// On startup, jobs left in <see cref="BulkOperationStatus.Running"/> mean the process was
    /// interrupted mid-batch (crash, deploy, container restart) — the in-memory queue and
    /// cancellation registry did not survive, so resuming mid-batch safely isn't possible
    /// without additional idempotency tracking this first version doesn't have. Mark them
    /// <see cref="BulkOperationStatus.Failed"/> with a clear reason rather than leaving them
    /// stuck showing "Running" forever. <see cref="BulkOperationStatus.Pending"/> jobs are
    /// re-enqueued fresh — they never started, so resuming them is simply starting them.
    /// </summary>
    private async Task RecoverInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

            var interrupted = await dbContext.BulkOperationJobs
                .Where(j => j.Status == BulkOperationStatus.Running)
                .ToListAsync(stoppingToken);

            foreach (var job in interrupted)
            {
                job.Status = BulkOperationStatus.Failed;
                job.ErrorSummary = "Interrupted by a server restart before it could finish.";
                job.CompletedAt = DateTimeOffset.UtcNow;
            }

            if (interrupted.Count > 0)
            {
                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogWarning(
                    "Marked {Count} bulk operation job(s) as Failed — interrupted by a prior restart",
                    interrupted.Count);
            }

            var pending = await dbContext.BulkOperationJobs
                .Where(j => j.Status == BulkOperationStatus.Pending)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);

            foreach (var jobId in pending)
            {
                _queue.Enqueue(jobId);
            }

            if (pending.Count > 0)
            {
                _logger.LogInformation(
                    "Re-queued {Count} pending bulk operation job(s) found at worker startup",
                    pending.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover interrupted bulk operation jobs at startup");
        }
    }
}
