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

public sealed class DriftDetectionWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IDriftDetectionService> _detectionMock = new();
    private readonly Mock<IDriftResultCache> _cacheMock = new();
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
        OwnerId = "owner-a",
        PillarKind = PillarKind.Prevent,
        ProposalKind = "DriftFinding",
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:DriftDetectionWorker",
        ProposerKind = PlaybookActorKind.System,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
    };

    private IServiceProvider BuildServiceProvider(bool registerEventBus = false, bool registerPlaybookLedger = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_detectionMock.Object);
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

    private static Namespace CreateTestNamespace() =>
        Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new DriftDetectionWorker(null!, EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new DriftDetectionWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<DriftDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunDetectionCycleAsync ──────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_NoActiveNamespaces_DoesNotCallDetection()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _detectionMock.Verify(
            d => d.DetectDriftAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _detectionMock.Verify(
            d => d.DetectDriftAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_FindingsFound_StoresThemInCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.SchemaShapeDrift, 60, "shape drift");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<DriftFinding>>(f => f.Contains(finding))), Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoFindings_DoesNotTouchCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(Array.Empty<DriftFinding>()));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<DriftFinding>>()), Times.Never);
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

        _detectionMock.Setup(d => d.DetectDriftAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Failure(Error.Internal("ERR", "boom")));

        var finding = DriftFinding.Create(okNs.Id, "queue-2", DriftFindingType.PayloadFormatDrift, 70, "format drift");
        _detectionMock.Setup(d => d.DetectDriftAsync(okNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<DriftFinding>>(f => f.Contains(finding))), Times.Once);
    }

    // ── Roadmap §5, I5 — Push ────────────────────────────────────────

    [Fact]
    public async Task RunDetectionCycleAsync_FindingAtOrAboveThreshold_PublishesInsightDetectedEvent()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.SchemaShapeDrift, 90, "shape changed");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.InsightDetected && e.NamespaceId == ns.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_FindingBelowThreshold_DoesNotPublish()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.SchemaShapeDrift, 40, "minor shift");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── PERSISTENCE-EVOLUTION-DESIGN §11 — Playbook Ledger proposals ─

    [Fact]
    public async Task RunDetectionCycleAsync_FindingAtOrAboveThreshold_ProposesPlaybookEntry()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.SchemaShapeDrift, 90, "shape changed");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        _playbookLedgerMock.Setup(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlaybookEntry>.Success(CreatePlaybookEntry()));

        var worker = new DriftDetectionWorker(BuildServiceProvider(registerPlaybookLedger: true), ConfigWithPushThreshold(70), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            l => l.ProposeAsync(
                It.Is<ProposePlaybookEntryRequest>(r =>
                    r.OwnerId == ns.OwnerId
                    && r.PillarKind == PillarKind.Prevent
                    && r.ProposalKind == "DriftFinding"
                    && r.NamespaceId == ns.Id
                    && r.Proposer.Kind == PlaybookActorKind.System),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_FindingBelowThreshold_DoesNotProposePlaybookEntry()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.SchemaShapeDrift, 40, "minor shift");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(registerPlaybookLedger: true), ConfigWithPushThreshold(70), NullLogger<DriftDetectionWorker>.Instance);

        await worker.RunDetectionCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDetectionCycleAsync_NoPlaybookLedgerRegistered_DoesNotThrow()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var finding = DriftFinding.Create(ns.Id, "queue-1", DriftFindingType.PayloadFormatDrift, 95, "format broke");
        _detectionMock.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new[] { finding }));

        var worker = new DriftDetectionWorker(BuildServiceProvider(registerPlaybookLedger: false), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        var act = () => worker.RunDetectionCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new DriftDetectionWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<DriftDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
