using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class BackupWorkerTests
{
    private readonly Mock<IBackupService> _backupServiceMock = new();

    private BackupWorker CreateWorker(BackupOptions options, TimeSpan? initialDelay = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_backupServiceMock.Object);
        var provider = services.BuildServiceProvider();

        return new BackupWorker(provider, Options.Create(options), NullLogger<BackupWorker>.Instance, initialDelay);
    }

    private static BackupManifest SampleManifest(string backupId = "20260828-000000Z") => new()
    {
        BackupId = backupId,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ServiceHubVersion = "test",
        Sqlite = new BackupFileInfo { FileName = "servicehub-dlq.db", SizeBytes = 100, Sha256 = "abc" },
        NamespaceStore = null,
        IntegrityCheck = "ok",
        EncryptionKeyFingerprint = "sha256:0000000000000000",
        ConsistencyNote = "note"
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new BackupWorker(null!, Options.Create(new BackupOptions()), NullLogger<BackupWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var act = () => new BackupWorker(provider, null!, NullLogger<BackupWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var act = () => new BackupWorker(provider, Options.Create(new BackupOptions()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Disabled (default, interval = 0) ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_IntervalZero_NeverCreatesBackup()
    {
        var worker = CreateWorker(new BackupOptions { ScheduledBackupIntervalHours = 0 });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        _backupServiceMock.Verify(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Enabled ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_IntervalPositive_CallsCreateBackup()
    {
        _backupServiceMock
            .Setup(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(SampleManifest()));

        var worker = CreateWorker(
            new BackupOptions { ScheduledBackupIntervalHours = 6 },
            initialDelay: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);
        cts.Cancel();

        _backupServiceMock.Verify(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_CreateBackupFails_DoesNotThrow_AndContinues()
    {
        _backupServiceMock
            .Setup(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BackupManifest>(Error.Internal("test.error", "simulated failure")));

        var worker = CreateWorker(
            new BackupOptions { ScheduledBackupIntervalHours = 6 },
            initialDelay: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        var act = async () =>
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_CreateBackupThrows_DoesNotCrashWorker()
    {
        _backupServiceMock
            .Setup(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var worker = CreateWorker(
            new BackupOptions { ScheduledBackupIntervalHours = 6 },
            initialDelay: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        var act = async () =>
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringInitialDelay_StopsGracefully()
    {
        var worker = CreateWorker(
            new BackupOptions { ScheduledBackupIntervalHours = 6 },
            initialDelay: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        _backupServiceMock.Verify(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
