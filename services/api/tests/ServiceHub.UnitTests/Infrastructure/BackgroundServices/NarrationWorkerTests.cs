using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class NarrationWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionMock = new();
    private readonly Mock<IDriftDetectionService> _driftDetectionMock = new();
    private readonly Mock<ICorrelationDetectionService> _correlationDetectionMock = new();
    private readonly Mock<INarrationService> _narrationServiceMock = new();
    private readonly Mock<INarrationResultCache> _cacheMock = new();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_anomalyDetectionMock.Object);
        services.AddSingleton(_driftDetectionMock.Object);
        services.AddSingleton(_correlationDetectionMock.Object);
        services.AddSingleton(_narrationServiceMock.Object);
        services.AddSingleton(_cacheMock.Object);
        return services.BuildServiceProvider();
    }

    private static Namespace CreateTestNamespace(string name = "test-namespace", string ownerId = Namespace.SpaOwnerId) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    private void SetupEmptyDetections(Namespace ns)
    {
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));
        _driftDetectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(Array.Empty<DriftFinding>()));
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new NarrationWorker(null!, EmptyConfig(), NullLogger<NarrationWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new NarrationWorker(Mock.Of<IServiceProvider>(), null!, NullLogger<NarrationWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new NarrationWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunNarrationCycleAsync ──────────────────────────────────────

    [Fact]
    public async Task RunNarrationCycleAsync_NoActiveNamespaces_DoesNotCallDetection()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        await worker.RunNarrationCycleAsync(CancellationToken.None);

        _anomalyDetectionMock.Verify(
            d => d.DetectAnomaliesAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunNarrationCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        var act = () => worker.RunNarrationCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunNarrationCycleAsync_NoFindings_DoesNotTouchCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));
        SetupEmptyDetections(ns);
        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());
        _narrationServiceMock.Setup(n => n.GenerateNarrations(
                It.IsAny<IReadOnlyDictionary<Guid, Namespace>>(),
                It.IsAny<IReadOnlyList<Anomaly>>(),
                It.IsAny<IReadOnlyList<DriftFinding>>(),
                It.IsAny<IReadOnlyList<CorrelationFinding>>()))
            .Returns(Array.Empty<Narration>());

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        await worker.RunNarrationCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<Narration>>()), Times.Never);
    }

    [Fact]
    public async Task RunNarrationCycleAsync_NarrationsGenerated_StoresInCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));
        _driftDetectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(Array.Empty<DriftFinding>()));
        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());

        var narration = Narration.Create(NarrationKind.NamespaceActivity, ns.Id, [ns.Id], "headline", "summary", 80);
        _narrationServiceMock.Setup(n => n.GenerateNarrations(
                It.IsAny<IReadOnlyDictionary<Guid, Namespace>>(),
                It.Is<IReadOnlyList<Anomaly>>(a => a.Contains(anomaly)),
                It.IsAny<IReadOnlyList<DriftFinding>>(),
                It.IsAny<IReadOnlyList<CorrelationFinding>>()))
            .Returns(new[] { narration });

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        await worker.RunNarrationCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<Narration>>(n => n.Contains(narration))), Times.Once);
    }

    [Fact]
    public async Task RunNarrationCycleAsync_AnomalyDetectionFailsForOneNamespace_ContinuesWithOthers()
    {
        var failingNs = CreateTestNamespace("failing-ns", "key_owner1");
        var okNs = CreateTestNamespace("ok-ns", "key_owner1");

        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { failingNs, okNs }));

        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Failure(Error.Internal("ERR", "boom")));
        SetupEmptyDetections(failingNs);
        _driftDetectionMock.Setup(d => d.DetectDriftAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(Array.Empty<DriftFinding>()));
        SetupEmptyDetections(okNs);

        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());
        _narrationServiceMock.Setup(n => n.GenerateNarrations(
                It.IsAny<IReadOnlyDictionary<Guid, Namespace>>(),
                It.IsAny<IReadOnlyList<Anomaly>>(),
                It.IsAny<IReadOnlyList<DriftFinding>>(),
                It.IsAny<IReadOnlyList<CorrelationFinding>>()))
            .Returns(Array.Empty<Narration>());

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        var act = () => worker.RunNarrationCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new NarrationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<NarrationWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
