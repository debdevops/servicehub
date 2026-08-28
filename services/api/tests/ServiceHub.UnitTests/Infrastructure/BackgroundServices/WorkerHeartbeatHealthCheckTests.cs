using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceHub.Infrastructure.BackgroundServices;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class WorkerHeartbeatHealthCheckTests
{
    // Mirrors WorkerHeartbeatHealthCheck's own private ExpectedWorkers list — kept independent
    // (not reflected out of the type under test) so a change to one is caught by the other.
    private static readonly string[] AllWorkers =
    [
        nameof(AnomalyDetectionWorker),
        nameof(DriftDetectionWorker),
        nameof(CorrelationDetectionWorker),
        nameof(DlqMonitorWorker),
        nameof(BulkOperationWorker),
        nameof(SignatureReplayWorker),
        nameof(AuditRetentionWorker),
        nameof(RecoveryVerificationWorker),
        nameof(RecoveryAgeingWorker),
        nameof(AutonomyEvaluationWorker),
        nameof(BackupWorker),
    ];

    private static WorkerHeartbeatHealthCheck CreateSut(
        InMemoryWorkerHeartbeatStore store, WorkerHeartbeatHealthCheckOptions? options = null) =>
        new(store, options ?? WorkerHeartbeatHealthCheckOptions.Default);

    [Fact]
    public async Task CheckHealthAsync_NoHeartbeatsRecorded_ReportsDegradedNamingEveryWorker()
    {
        var store = new InMemoryWorkerHeartbeatStore();
        var sut = CreateSut(store);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain($"{AllWorkers.Length} worker(s) never reported");
        foreach (var worker in AllWorkers)
        {
            result.Data.Should().ContainKey(worker);
            result.Data[worker].Should().Be("never reported");
        }
    }

    [Fact]
    public async Task CheckHealthAsync_AllWorkersFreshWithinCadence_ReportsHealthy()
    {
        var store = new InMemoryWorkerHeartbeatStore();
        foreach (var worker in AllWorkers)
        {
            store.RecordHeartbeat(worker, TimeSpan.FromMinutes(1));
        }

        var sut = CreateSut(store);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().HaveCount(AllWorkers.Length);
    }

    [Fact]
    public async Task CheckHealthAsync_OneWorkerStale_ReportsDegradedNamingOnlyThatWorker()
    {
        var staleStore = new InMemoryWorkerHeartbeatStore();
        foreach (var worker in AllWorkers)
        {
            staleStore.RecordHeartbeat(worker, TimeSpan.FromMinutes(1));
        }

        var sut = CreateSut(staleStore);

        // Force staleness by using a near-zero interval for a single worker so "just recorded"
        // already exceeds interval * multiplier (default 3x).
        staleStore.RecordHeartbeat(nameof(DlqMonitorWorker), TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("1 worker(s) stale");
        result.Description.Should().Contain(nameof(DlqMonitorWorker));
        result.Data[nameof(DlqMonitorWorker)].ToString().Should().Contain("stale");
        result.Data[nameof(AutonomyEvaluationWorker)].ToString().Should().Contain("ok");
    }

    [Fact]
    public async Task CheckHealthAsync_NullExpectedInterval_NeverReportsStaleRegardlessOfAge()
    {
        var store = new InMemoryWorkerHeartbeatStore();
        foreach (var worker in AllWorkers)
        {
            store.RecordHeartbeat(worker, expectedInterval: worker == nameof(BackupWorker) ? null : TimeSpan.FromMinutes(1));
        }

        var sut = CreateSut(store);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data[nameof(BackupWorker)].ToString().Should().Contain("ok");
    }

    [Fact]
    public async Task CheckHealthAsync_NeverReturnsUnhealthy_EvenWhenEveryWorkerIsMissing()
    {
        var store = new InMemoryWorkerHeartbeatStore();
        var sut = CreateSut(store);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().NotBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_CustomStalenessMultiplier_IsRespected()
    {
        var store = new InMemoryWorkerHeartbeatStore();
        foreach (var worker in AllWorkers)
        {
            store.RecordHeartbeat(worker, TimeSpan.FromMilliseconds(20));
        }

        await Task.Delay(60);

        // With a generous multiplier, 60ms after a 20ms interval is still within budget.
        var lenientSut = CreateSut(store, new WorkerHeartbeatHealthCheckOptions { StalenessMultiplier = 100.0 });
        var lenientResult = await lenientSut.CheckHealthAsync(new HealthCheckContext());
        lenientResult.Status.Should().Be(HealthStatus.Healthy);

        // With a tight multiplier, the same age is stale.
        var strictSut = CreateSut(store, new WorkerHeartbeatHealthCheckOptions { StalenessMultiplier = 1.0 });
        var strictResult = await strictSut.CheckHealthAsync(new HealthCheckContext());
        strictResult.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var store = new InMemoryWorkerHeartbeatStore();

        var actStore = () => new WorkerHeartbeatHealthCheck(null!, WorkerHeartbeatHealthCheckOptions.Default);
        var actOptions = () => new WorkerHeartbeatHealthCheck(store, null!);

        actStore.Should().Throw<ArgumentNullException>();
        actOptions.Should().Throw<ArgumentNullException>();
    }
}
