using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class AnomalyDetectionWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IAnomalyDetectionService> _detectionMock = new();
    private readonly Mock<IAnomalyResultCache> _cacheMock = new();
    private readonly Mock<IPlatformEventBus> _eventBusMock = new();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static IConfiguration ConfigWithPushThreshold(int threshold) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Insight:PushSeverityThreshold"] = threshold.ToString() })
            .Build();

    private IServiceProvider BuildServiceProvider(bool registerEventBus = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_detectionMock.Object);
        services.AddSingleton(_cacheMock.Object);
        if (registerEventBus)
        {
            services.AddSingleton(_eventBusMock.Object);
        }
        return services.BuildServiceProvider();
    }

    private static Namespace CreateTestNamespace() =>
        Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new AnomalyDetectionWorker(null!, EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new AnomalyDetectionWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<AnomalyDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunDetectionCycleAsync ──────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_NoActiveNamespaces_DoesNotCallDetection()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _detectionMock.Verify(
            d => d.DetectAnomaliesAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _detectionMock.Verify(
            d => d.DetectAnomaliesAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_AnomaliesFound_StoresThemInCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _detectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<Anomaly>>(a => a.Contains(anomaly))), Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoAnomaliesFound_DoesNotTouchCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _detectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<Anomaly>>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_DetectionFailsForOneNamespace_ContinuesWithOthers()
    {
        var failingNs = CreateTestNamespace();
        var okNs = Namespace.Create(
            "test-namespace-2",
            "Endpoint=sb://test2.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS 2").Value;

        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { failingNs, okNs }));

        _detectionMock.Setup(d => d.DetectAnomaliesAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Failure(Error.Internal("ERR", "boom")));

        var anomaly = Anomaly.Create(okNs.Id, "queue-2", AnomalyType.LowMessageVolume, 40, "drop");
        _detectionMock.Setup(d => d.DetectAnomaliesAsync(okNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<Anomaly>>(a => a.Contains(anomaly))), Times.Once);
    }

    // ── Roadmap §5, I5 — Push ────────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_AnomalyAtOrAboveThreshold_PublishesInsightDetectedEvent()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _detectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.InsightDetected && e.NamespaceId == ns.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_AnomalyBelowThreshold_DoesNotPublish()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.LowMessageVolume, 40, "drop");
        _detectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<AnomalyDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoEventBusRegistered_DoesNotThrow()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 95, "spike");
        _detectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(registerEventBus: false), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new AnomalyDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<AnomalyDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
