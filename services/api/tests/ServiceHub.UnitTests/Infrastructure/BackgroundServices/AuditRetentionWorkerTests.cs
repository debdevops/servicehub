using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class AuditRetentionWorkerTests
{
    private readonly Mock<IAuditService> _auditServiceMock = new();

    private AuditRetentionWorker CreateWorker(AuditRetentionOptions options, TimeSpan? initialDelay = null) =>
        new(_auditServiceMock.Object, Options.Create(options), NullLogger<AuditRetentionWorker>.Instance, initialDelay);

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullAuditService_Throws()
    {
        var act = () => new AuditRetentionWorker(null!, Options.Create(new AuditRetentionOptions()), NullLogger<AuditRetentionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("auditService");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new AuditRetentionWorker(_auditServiceMock.Object, null!, NullLogger<AuditRetentionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AuditRetentionWorker(_auditServiceMock.Object, Options.Create(new AuditRetentionOptions()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Disabled (default) ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Disabled_NeverCallsPurge()
    {
        var worker = CreateWorker(new AuditRetentionOptions { Enabled = false });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        _auditServiceMock.Verify(
            s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Enabled ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Enabled_CallsPurgeWithCorrectCutoff()
    {
        DateTimeOffset? capturedCutoff = null;
        _auditServiceMock
            .Setup(s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, CancellationToken>((cutoff, _) => capturedCutoff = cutoff)
            .ReturnsAsync(Result<int>.Success(0));

        var worker = CreateWorker(
            new AuditRetentionOptions { Enabled = true, RetentionDays = 90, SweepIntervalHours = 24 },
            initialDelay: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);
        cts.Cancel();

        _auditServiceMock.Verify(
            s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        capturedCutoff.Should().NotBeNull();
        capturedCutoff!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-90), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExecuteAsync_PurgeFails_DoesNotThrow_AndContinues()
    {
        _auditServiceMock
            .Setup(s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.Internal("test.error", "simulated failure")));

        var worker = CreateWorker(
            new AuditRetentionOptions { Enabled = true, RetentionDays = 30, SweepIntervalHours = 24 },
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
    public async Task ExecuteAsync_PurgeThrows_DoesNotCrashWorker()
    {
        _auditServiceMock
            .Setup(s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var worker = CreateWorker(
            new AuditRetentionOptions { Enabled = true, RetentionDays = 30, SweepIntervalHours = 24 },
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
            new AuditRetentionOptions { Enabled = true, RetentionDays = 30 },
            initialDelay: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        _auditServiceMock.Verify(
            s => s.PurgeExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
