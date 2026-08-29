using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class ExternalSignalCorrelationWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionMock = new();
    private readonly Mock<IExternalSignalCorrelationService> _correlationServiceMock = new();
    private readonly Mock<IExternalSignalRepository> _signalRepoMock = new();
    private readonly Mock<IExternalSignalCorrelationCache> _cacheMock = new();
    private readonly Mock<IPlatformEventBus> _eventBusMock = new();
    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static IConfiguration ConfigWithPushThreshold(int threshold) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Insight:PushSeverityThreshold"] = threshold.ToString() })
            .Build();

    private static PlaybookEntry CreatePlaybookEntry() => new()
    {
        OwnerId = "key_owner1",
        PillarKind = PillarKind.Correlate,
        ProposalKind = "CorrelationHypothesis",
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:ExternalSignalCorrelationWorker",
        ProposerKind = PlaybookActorKind.System,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
    };

    private IServiceProvider BuildServiceProvider(bool registerEventBus = false, bool registerPlaybookLedger = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_anomalyDetectionMock.Object);
        services.AddSingleton(_correlationServiceMock.Object);
        services.AddSingleton(_signalRepoMock.Object);
        services.AddSingleton(_cacheMock.Object);
        if (registerEventBus)
        {
            services.AddSingleton(_eventBusMock.Object);
        }
        if (registerPlaybookLedger)
        {
            services.AddSingleton(_playbookLedgerMock.Object);
        }
        return services.BuildServiceProvider();
    }

    private static Namespace CreateTestNamespace(string name = "test-namespace", string ownerId = Namespace.SpaOwnerId) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    private static ExternalSignalEvent CreateSignal(string ownerId, Guid? namespaceId = null) => new()
    {
        OwnerId = ownerId,
        NamespaceId = namespaceId,
        SignalType = ExternalSignalType.Deploy,
        OccurredAt = DateTimeOffset.UtcNow.AddHours(-1),
        Source = "webhook:github-actions",
        IngestedAt = DateTimeOffset.UtcNow,
    };

    private static ExternalSignalCorrelation CreateCorrelation(
        string ownerId, Guid namespaceId, string entityName, int severity, ExternalSignalEvent signal) =>
        ExternalSignalCorrelation.Create(
            ownerId, namespaceId, entityName, AnomalyType.HighMessageVolume, severity,
            CloudProviderType.Azure, signal, TimeSpan.FromMinutes(30), "correlated");

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new ExternalSignalCorrelationWorker(null!, EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new ExternalSignalCorrelationWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<ExternalSignalCorrelationWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunDetectionCycleAsync ──────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_NoActiveNamespaces_DoesNotCallAnomalyDetection()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

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

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoAnomalies_DoesNotQuerySignalsOrCorrelate()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _signalRepoMock.Verify(
            r => r.QueryAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _correlationServiceMock.Verify(
            c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoSignalsForOwner_DoesNotCallCorrelationService()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ExternalSignalEvent>());

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _correlationServiceMock.Verify(
            c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_OwnersCorrelatedIndependently_EachOwnerQueriesOnlyItsOwnSignals()
    {
        var nsOwner1 = CreateTestNamespace("ns-owner1", "key_owner1");
        var nsOwner2 = CreateTestNamespace("ns-owner2", "key_owner2");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { nsOwner1, nsOwner2 }));

        var anomaly1 = Anomaly.Create(nsOwner1.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        var anomaly2 = Anomaly.Create(nsOwner2.Id, "queue-2", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(nsOwner1.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly1 }));
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(nsOwner2.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly2 }));

        var signal1 = CreateSignal("key_owner1");
        var signal2 = CreateSignal("key_owner2");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal1 });
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner2", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal2 });

        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(Array.Empty<ExternalSignalCorrelation>());

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _signalRepoMock.Verify(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _signalRepoMock.Verify(r => r.QueryAsync("key_owner2", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _correlationServiceMock.Verify(
            c => c.DetectCorrelations(It.Is<IReadOnlyList<AnomalyObservation>>(o => o.All(x => x.OwnerId == "key_owner1")), It.Is<IReadOnlyList<ExternalSignalEvent>>(s => s.Contains(signal1)), It.IsAny<TimeSpan>()),
            Times.Once);
        _correlationServiceMock.Verify(
            c => c.DetectCorrelations(It.Is<IReadOnlyList<AnomalyObservation>>(o => o.All(x => x.OwnerId == "key_owner2")), It.Is<IReadOnlyList<ExternalSignalEvent>>(s => s.Contains(signal2)), It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_CorrelationsFound_StoresInCache()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 80, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<ExternalSignalCorrelation>>(f => f.Contains(correlation))), Times.Once);
    }

    // ── Roadmap §5, I5 — Push ────────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_CorrelationAtOrAboveThreshold_PublishesInsightDetectedEvent()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 80, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e =>
                    e.EventType == EventTypes.InsightDetected
                    && e.Actor == "key_owner1"
                    && e.NamespaceId == ns.Id
                    && e.TargetScope == "queue-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_CorrelationBelowThreshold_DoesNotPublish()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 40, "minor");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 40, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── PERSISTENCE-EVOLUTION-DESIGN §12 — Playbook Ledger proposals ─

    [Fact]
    public async Task RunDetectionCycleAsync_CorrelationAtOrAboveThreshold_ProposesPlaybookEntry()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 80, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        _playbookLedgerMock.Setup(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlaybookEntry>.Success(CreatePlaybookEntry()));

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(registerPlaybookLedger: true), ConfigWithPushThreshold(70), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            l => l.ProposeAsync(
                It.Is<ProposePlaybookEntryRequest>(r =>
                    r.OwnerId == "key_owner1"
                    && r.PillarKind == PillarKind.Correlate
                    && r.ProposalKind == "CorrelationHypothesis"
                    && r.NamespaceId == ns.Id
                    && r.NamespaceNameSnapshot == ns.Name
                    && r.ProviderSnapshot == correlation.Provider
                    && r.Proposer.Kind == PlaybookActorKind.System
                    && r.Proposer.Identity == "System:ExternalSignalCorrelationWorker"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_CorrelationBelowThreshold_DoesNotProposePlaybookEntry()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 40, "minor");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 40, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(registerPlaybookLedger: true), ConfigWithPushThreshold(70), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoPlaybookLedgerRegistered_DoesNotThrow()
    {
        var ns = CreateTestNamespace(ownerId: "key_owner1");
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var anomaly = Anomaly.Create(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 95, "spike");
        _anomalyDetectionMock.Setup(d => d.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new[] { anomaly }));

        var signal = CreateSignal("key_owner1");
        _signalRepoMock.Setup(r => r.QueryAsync("key_owner1", null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var correlation = CreateCorrelation("key_owner1", ns.Id, "queue-1", 95, signal);
        _correlationServiceMock.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(registerPlaybookLedger: false), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new ExternalSignalCorrelationWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<ExternalSignalCorrelationWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
