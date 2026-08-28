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

public sealed class CorrelationDetectionWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionMock = new();
    private readonly Mock<ICorrelationDetectionService> _correlationDetectionMock = new();
    private readonly Mock<ICorrelationResultCache> _cacheMock = new();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_anomalyDetectionMock.Object);
        services.AddSingleton(_correlationDetectionMock.Object);
        services.AddSingleton(_cacheMock.Object);
        return services.BuildServiceProvider();
    }

    private static Namespace CreateTestNamespace(string name = "test-namespace", string ownerId = Namespace.SpaOwnerId) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new CorrelationDetectionWorker(null!, EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new CorrelationDetectionWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<CorrelationDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunDetectionCycleAsync ──────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_NoActiveNamespaces_DoesNotCallAnomalyDetection()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _anomalyDetectionMock.Verify(
            d => d.DetectAnomaliesAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoAnomaliesAcrossNamespaces_DoesNotCallCorrelationDetection()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _correlationDetectionMock.Verify(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()), Times.Never);
        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<CorrelationFinding>>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_AnomaliesAcrossTwoNamespaces_PassesTaggedObservationsToCorrelationService()
    {
        var nsA = CreateTestNamespace("ns-a", "key_owner1");
        var nsB = CreateTestNamespace("ns-b", "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { nsA, nsB }));

        var anomalyA = Anomaly.Create(nsA.Id, "queue-a", AnomalyType.HighMessageVolume, 80, "spike");
        var anomalyB = Anomaly.Create(nsB.Id, "queue-b", AnomalyType.HighMessageVolume, 60, "spike");

        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(nsA.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomalyA }));
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(nsB.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomalyB }));

        var correlationFinding = CorrelationFinding.Create(
            "key_owner1", CloudProviderType.Azure,
            new[]
            {
                new CorrelationMember(nsA.Id, "queue-a", AnomalyType.HighMessageVolume, 80),
                new CorrelationMember(nsB.Id, "queue-b", AnomalyType.HighMessageVolume, 60),
            },
            80, "correlated");

        IReadOnlyList<AnomalyObservation>? capturedObservations = null;
        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Callback<IReadOnlyList<AnomalyObservation>>(obs => capturedObservations = obs)
            .Returns(new[] { correlationFinding });

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        capturedObservations.Should().HaveCount(2);
        capturedObservations.Should().Contain(o => o.Anomaly == anomalyA && o.OwnerId == "key_owner1" && o.Provider == nsA.Provider);
        capturedObservations.Should().Contain(o => o.Anomaly == anomalyB && o.OwnerId == "key_owner1" && o.Provider == nsB.Provider);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<CorrelationFinding>>(f => f.Contains(correlationFinding))), Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoCorrelationsFound_DoesNotTouchCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<CorrelationFinding>>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_AnomalyDetectionFailsForOneNamespace_ContinuesWithOthers()
    {
        var failingNs = CreateTestNamespace("failing-ns", "key_owner1");
        var okNs = CreateTestNamespace("ok-ns", "key_owner1");

        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { failingNs, okNs }));

        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Failure(Error.Internal("ERR", "boom")));

        var anomaly = Anomaly.Create(okNs.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(okNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        _correlationDetectionMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _correlationDetectionMock.Verify(c => c.DetectCorrelations(It.Is<IReadOnlyList<AnomalyObservation>>(o => o.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new CorrelationDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<CorrelationDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
