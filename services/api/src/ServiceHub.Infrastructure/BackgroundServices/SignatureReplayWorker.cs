using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Drains <see cref="ISignatureReplayQueue"/> and processes signature-replay jobs one at a time
/// via a fresh <see cref="ISignatureReplayExecutor"/> scope per job. Mirrors
/// <see cref="BulkOperationWorker"/> exactly, including its single-concurrency rationale and
/// restart-recovery approach.
/// </summary>
public sealed class SignatureReplayWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISignatureReplayQueue _queue;
    private readonly ILogger<SignatureReplayWorker> _logger;

    /// <summary>Initializes a new instance of the <see cref="SignatureReplayWorker"/> class.</summary>
    public SignatureReplayWorker(
        IServiceProvider serviceProvider,
        ISignatureReplayQueue queue,
        ILogger<SignatureReplayWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

        await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
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
                _logger.LogError(ex, "Unhandled error processing signature replay job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, _queue.RegisterRunning(jobId));

        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISignatureReplayExecutor>();

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
    /// interrupted mid-batch — mark them <see cref="BulkOperationStatus.Failed"/> with a clear
    /// reason rather than leaving them stuck showing "Running" forever.
    /// <see cref="BulkOperationStatus.Pending"/> jobs are re-enqueued fresh — they never
    /// started, so resuming them is simply starting them.
    /// </summary>
    private async Task RecoverInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

            var interrupted = await dbContext.SignatureReplayJobs
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
                    "Marked {Count} signature replay job(s) as Failed — interrupted by a prior restart",
                    interrupted.Count);
            }

            var pending = await dbContext.SignatureReplayJobs
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
                    "Re-queued {Count} pending signature replay job(s) found at worker startup",
                    pending.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover interrupted signature replay jobs at startup");
        }
    }
}
