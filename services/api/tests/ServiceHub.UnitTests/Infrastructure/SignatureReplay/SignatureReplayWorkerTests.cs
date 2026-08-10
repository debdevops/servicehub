using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.SignatureReplay;

namespace ServiceHub.UnitTests.Infrastructure.SignatureReplay;

/// <summary>
/// Exercises <see cref="SignatureReplayWorker"/>'s startup restart-recovery behavior — jobs left
/// <c>Running</c> when the process died are marked <c>Failed</c>, and jobs still <c>Pending</c>
/// are re-enqueued — the same durability guarantee <c>BulkOperationWorker</c> already provides.
/// </summary>
public sealed class SignatureReplayWorkerTests : IDisposable
{
    // A uniquely named shared-cache in-memory database, not "DataSource=:memory:" over one shared
    // SqliteConnection. Sharing a single open connection across scopes made every DbContext EF
    // built on the worker thread call sqlite3_create_function on a handle the test thread might
    // have an open reader on, which fails with SQLITE_BUSY ("unable to delete/modify user-function
    // due to active statements"). Each scope now opens its own connection, matching production,
    // where DlqDbContext is registered against a file path.
    private readonly string _connectionString =
        $"Data Source=signature-replay-worker-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // Shared-cache in-memory databases live only while at least one connection to them is open.
    private readonly SqliteConnection _keepAliveConnection;
    private readonly ServiceProvider _serviceProvider;

    public SignatureReplayWorkerTests()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<DlqDbContext>(options => options.UseSqlite(_connectionString));
        services.AddSingleton<ISignatureReplayQueue, RecoverySignallingQueue>();
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DlqDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _keepAliveConnection.Dispose();
    }

    /// <summary>
    /// Wraps the real <see cref="SignatureReplayQueue"/> and exposes the exact moment startup
    /// recovery finished. <c>ExecuteAsync</c> awaits <c>RecoverInterruptedJobsAsync</c> to
    /// completion and only then calls <see cref="DequeueAllAsync"/>, so that call is a precise
    /// happens-after signal — no polling, sleeping, or timeout guessing required.
    /// </summary>
    private sealed class RecoverySignallingQueue : ISignatureReplayQueue
    {
        private readonly SignatureReplayQueue _inner = new();
        private readonly TaskCompletionSource _recoveryCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<Guid> _enqueued = new();

        public Task RecoveryCompleted => _recoveryCompleted.Task;

        public IReadOnlyCollection<Guid> Enqueued => _enqueued.ToArray();

        public void Enqueue(Guid jobId)
        {
            _enqueued.Enqueue(jobId);
            _inner.Enqueue(jobId);
        }

        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        {
            _recoveryCompleted.TrySetResult();
            return _inner.DequeueAllAsync(cancellationToken);
        }

        public CancellationToken RegisterRunning(Guid jobId) => _inner.RegisterRunning(jobId);

        public void RequestCancellation(Guid jobId) => _inner.RequestCancellation(jobId);

        public void Complete(Guid jobId) => _inner.Complete(jobId);
    }

    private SignatureReplayJob SeedJob(BulkOperationStatus status)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = "entra:test-owner",
            Status = status,
            NamespaceId = Guid.NewGuid(),
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = status == BulkOperationStatus.Running ? DateTimeOffset.UtcNow : null,
        };
        dbContext.SignatureReplayJobs.Add(job);
        dbContext.SaveChanges();
        return job;
    }

    private async Task<SignatureReplayJob> ReloadAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        return await dbContext.SignatureReplayJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
    }

    [Fact]
    public async Task Startup_InterruptedRunningJob_IsMarkedFailedWithExplanatoryMessage()
    {
        var runningJob = SeedJob(BulkOperationStatus.Running);
        var queue = (RecoverySignallingQueue)_serviceProvider.GetRequiredService<ISignatureReplayQueue>();
        var worker = new SignatureReplayWorker(_serviceProvider, queue, NullLogger<SignatureReplayWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await queue.RecoveryCompleted;

        var reloaded = await ReloadAsync(runningJob.Id);

        reloaded.Status.Should().Be(BulkOperationStatus.Failed);
        reloaded.ErrorSummary.Should().Contain("restart");
        reloaded.CompletedAt.Should().NotBeNull();

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_PendingJob_IsReenqueuedNotMutated()
    {
        var pendingJob = SeedJob(BulkOperationStatus.Pending);
        var queue = (RecoverySignallingQueue)_serviceProvider.GetRequiredService<ISignatureReplayQueue>();
        var worker = new SignatureReplayWorker(_serviceProvider, queue, NullLogger<SignatureReplayWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await queue.RecoveryCompleted;

        // Recovery re-enqueues rather than mutating: the job is handed back to the queue for a
        // fresh run, and its row is left untouched so the executor still sees a Pending job.
        queue.Enqueued.Should().ContainSingle().Which.Should().Be(pendingJob.Id);

        var reloaded = await ReloadAsync(pendingJob.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Pending);
        reloaded.StartedAt.Should().BeNull();
        reloaded.CompletedAt.Should().BeNull();
        reloaded.ErrorSummary.Should().BeNull();

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }
}
