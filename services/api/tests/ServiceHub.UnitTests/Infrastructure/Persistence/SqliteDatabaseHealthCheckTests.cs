using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

public sealed class SqliteDatabaseHealthCheckTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly DlqDbContext _dbContext;

    public SqliteDatabaseHealthCheckTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"servicehub-dbhealth-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "servicehub-dlq.db");

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(SqlitePragmaConnectionInterceptor.DefaultBusyTimeoutMilliseconds))
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private SqliteDatabaseHealthCheck CreateHealthCheck(SqliteDatabaseHealthCheckOptions? options = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DlqDatabase:DataDirectory"] = _tempRoot
            })
            .Build();

        return new SqliteDatabaseHealthCheck(
            _dbContext,
            configuration,
            options ?? SqliteDatabaseHealthCheckOptions.Default,
            NullLogger<SqliteDatabaseHealthCheck>.Instance);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthyDatabase_ReportsHealthyWithSizeAndWalData()
    {
        var healthCheck = CreateHealthCheck();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("DataDirectory");
        result.Data["DataDirectory"].Should().Be(_tempRoot);
        result.Data.Should().ContainKey("DatabaseSizeBytes");
        result.Data.Should().ContainKey("WalSizeBytes");
        result.Data.Should().ContainKey("TotalSizeBytes");
        result.Data.Should().ContainKey("JournalMode");
        result.Data["JournalMode"].Should().Be("wal");
        result.Data.Should().ContainKey("WalCheckpointBusy");
        result.Data.Should().ContainKey("WalLogFrames");
        result.Data.Should().ContainKey("WalCheckpointedFrames");
        result.Data.Should().ContainKey("CheckDurationMs");
        ((long)result.Data["DatabaseSizeBytes"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CheckHealthAsync_DatabaseFileMissing_ReportsUnhealthy()
    {
        _dbContext.Dispose();
        File.Delete(_dbPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DlqDatabase:DataDirectory"] = _tempRoot
            })
            .Build();

        // A fresh context whose connection is never opened, so no new file is created merely by
        // constructing it — matching a real "file went missing underneath a running process".
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var contextOverMissingFile = new DlqDbContext(options);

        var healthCheck = new SqliteDatabaseHealthCheck(
            contextOverMissingFile,
            configuration,
            SqliteDatabaseHealthCheckOptions.Default,
            NullLogger<SqliteDatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not found");
        result.Data.Should().ContainKey("DataDirectory");
        result.Data["DataDirectory"].Should().Be(_tempRoot);
    }

    [Fact]
    public async Task CheckHealthAsync_WalSizeExceedsThreshold_ReportsDegraded()
    {
        // Write enough rows to grow the WAL file past a threshold set well below any real
        // write, without needing to fabricate a huge payload.
        for (var i = 0; i < 20; i++)
        {
            _dbContext.DlqMessages.Add(new DlqMessage
            {
                MessageId = $"msg-{i}",
                SequenceNumber = i,
                BodyHash = $"hash-{i}",
                NamespaceId = Guid.NewGuid(),
                OwnerId = "owner",
                EntityName = "queue",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow,
                DetectedAtUtc = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                MessageSize = 50
            });
        }
        await _dbContext.SaveChangesAsync();

        var walSizeBytes = new FileInfo(_dbPath + "-wal").Length;
        walSizeBytes.Should().BeGreaterThan(0, "WAL mode should leave a non-empty -wal file before a checkpoint reclaims it");

        var healthCheck = CreateHealthCheck(new SqliteDatabaseHealthCheckOptions
        {
            WalSizeWarningThresholdBytes = walSizeBytes - 1
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("WAL file size");
    }

    [Fact]
    public async Task CheckHealthAsync_SlowCheckThresholdExceeded_LogsWarningAndReportsDegraded()
    {
        // A zero-duration threshold guarantees the check's own (non-zero) elapsed time exceeds
        // it, deterministically exercising the slow-query-equivalent path without needing to
        // actually slow down SQLite itself.
        var healthCheck = CreateHealthCheck(new SqliteDatabaseHealthCheckOptions
        {
            SlowCheckThreshold = TimeSpan.Zero
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("threshold");
    }
}
