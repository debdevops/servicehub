using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class ReasoningCompanionWorkerTests
{
    private readonly Mock<INamespaceRepository> _namespaceRepoMock = new();
    private readonly Mock<IAttentionQueueService> _attentionQueueMock = new();
    private readonly Mock<IIncidentReadModelService> _incidentReadModelMock = new();
    private readonly Mock<IReasoningAgentClient> _reasoningAgentClientMock = new();
    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();

    private static ReasoningAgentOptions EnabledOptions() => new()
    {
        Enabled = true,
        ServiceUrl = "http://agent.internal:8010",
        SweepIntervalMinutes = 60,
        MaxSignaturesPerSweep = 3,
    };

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_namespaceRepoMock.Object);
        services.AddSingleton(_attentionQueueMock.Object);
        services.AddSingleton(_incidentReadModelMock.Object);
        services.AddSingleton(_reasoningAgentClientMock.Object);
        services.AddSingleton(_playbookLedgerMock.Object);
        return services.BuildServiceProvider();
    }

    private static ReasoningCompanionWorker CreateWorker(ReasoningAgentOptions options, IServiceProvider serviceProvider) =>
        new(serviceProvider, Options.Create(options), NullLogger<ReasoningCompanionWorker>.Instance);

    private static Namespace CreateTestNamespace(string name = "test-namespace", string ownerId = Namespace.SpaOwnerId) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    private static AttentionQueueItem CreateAttentionItem(Guid namespaceId, string signatureHash) => new(
        SignatureHash: signatureHash,
        NamespaceId: namespaceId,
        NamespaceName: "orders-namespace",
        DisplayName: signatureHash,
        LifecycleStatus: "Active",
        Severity: "Critical",
        BlastRadius: 10,
        IsRecurring: true,
        PendingDecisionCount: 1,
        Score: 90,
        RecommendedAction: "Review",
        LastSeenAt: DateTimeOffset.UtcNow);

    private static IncidentDetailResponse CreateIncident(Guid namespaceId, string signatureHash) => new(
        SignatureHash: signatureHash,
        NamespaceId: namespaceId,
        NamespaceName: "orders-namespace",
        LifecycleStatus: "Active",
        FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-1),
        LastSeenAt: DateTimeOffset.UtcNow,
        OccurrenceCount: 10,
        DominantDeadletterReason: "MaxDeliveryCountExceeded",
        TopTerms: ["timeout"],
        Summary: new IncidentSummary(3, 1, 1, 1, 0, 0, 0, 0),
        RecoveryEntries: [],
        PlaybookEntries: []);

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new ReasoningCompanionWorker(null!, Options.Create(EnabledOptions()), NullLogger<ReasoningCompanionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new ReasoningCompanionWorker(Mock.Of<IServiceProvider>(), null!, NullLogger<ReasoningCompanionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ReasoningCompanionWorker(Mock.Of<IServiceProvider>(), Options.Create(EnabledOptions()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunSweepCycleAsync ──────────────────────────────────────────

    [Fact]
    public async Task RunSweepCycleAsync_NoActiveNamespaces_DoesNotCallAttentionQueue()
    {
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        _attentionQueueMock.Verify(a => a.GetAttentionQueueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSweepCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("test.error", "boom")));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        var act = () => worker.RunSweepCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunSweepCycleAsync_AttentionQueueEmpty_DoesNotCallIncidentReadModel()
    {
        var ns = CreateTestNamespace();
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse([], IsEmpty: true)));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        _incidentReadModelMock.Verify(
            i => i.GetIncidentAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunSweepCycleAsync_AttentionQueueFails_DoesNotThrow()
    {
        var ns = CreateTestNamespace();
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AttentionQueueResponse>(Error.Internal("test.error", "boom")));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        var act = () => worker.RunSweepCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunSweepCycleAsync_CapsCandidatesAtMaxSignaturesPerSweep()
    {
        var ns = CreateTestNamespace();
        var items = Enumerable.Range(0, 5)
            .Select(i => CreateAttentionItem(ns.Id, $"sig-{i}"))
            .ToList();
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid nsId, string sig, CancellationToken _) => Result.Success(CreateIncident(nsId, sig)));
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>([]));

        var options = EnabledOptions();
        options.MaxSignaturesPerSweep = 2;
        var worker = CreateWorker(options, BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        _incidentReadModelMock.Verify(
            i => i.GetIncidentAsync(ns.OwnerId, ns.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunSweepCycleAsync_IncidentFetchFails_SkipsThatSignatureButContinues()
    {
        var ns = CreateTestNamespace();
        var items = new[] { CreateAttentionItem(ns.Id, "sig-bad"), CreateAttentionItem(ns.Id, "sig-good") };
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IncidentDetailResponse>(Error.NotFound("test.notfound", "missing")));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-good", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateIncident(ns.Id, "sig-good")));

        IReadOnlyList<ReasoningEvidenceRecord>? capturedEvidence = null;
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ReasoningEvidenceRecord>, CancellationToken>((e, _) => capturedEvidence = e)
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>([]));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        capturedEvidence.Should().ContainSingle();
        capturedEvidence![0].SignatureHash.Should().Be("sig-good");
    }

    [Fact]
    public async Task RunSweepCycleAsync_NoProposalsReturned_DoesNotCallPlaybookLedger()
    {
        var ns = CreateTestNamespace();
        var items = new[] { CreateAttentionItem(ns.Id, "sig-1") };
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateIncident(ns.Id, "sig-1")));
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>([]));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunSweepCycleAsync_ProposalReturned_ProposesPlaybookEntryWithReasoningAgentActor()
    {
        var ns = CreateTestNamespace();
        var items = new[] { CreateAttentionItem(ns.Id, "sig-1") };
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateIncident(ns.Id, "sig-1")));

        var expectedRef = global::ServiceHub.Infrastructure.Agent.ReasoningEvidenceMapper.BuildRef(ns.Id, "sig-1");
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>(
                [new ReasoningProposal(expectedRef, "This signature looks worth a look.", ["Consider X."])]));

        ProposePlaybookEntryRequest? capturedRequest = null;
        _playbookLedgerMock
            .Setup(p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProposePlaybookEntryRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(Result.Success(new PlaybookEntry
            {
                OwnerId = ns.OwnerId,
                PillarKind = PillarKind.Investigate,
                ProposalKind = "ReasoningCompanionObservation",
                EvidenceRefJson = "{}",
                ProposalJson = "{}",
                ProposedAt = DateTimeOffset.UtcNow,
                ProposerIdentity = "ReasoningAgent:services/agent",
                ProposerKind = PlaybookActorKind.ReasoningAgent,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            }));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.OwnerId.Should().Be(ns.OwnerId);
        capturedRequest.PillarKind.Should().Be(PillarKind.Investigate);
        capturedRequest.ProposalKind.Should().Be("ReasoningCompanionObservation");
        capturedRequest.SignatureHashSnapshot.Should().Be("sig-1");
        capturedRequest.NamespaceId.Should().Be(ns.Id);
        capturedRequest.Proposer.Kind.Should().Be(PlaybookActorKind.ReasoningAgent);
        capturedRequest.ProposalJson.Should().Contain("This signature looks worth a look.");
    }

    [Fact]
    public async Task RunSweepCycleAsync_ProposalWithUnrecognisedRef_IsSkipped()
    {
        var ns = CreateTestNamespace();
        var items = new[] { CreateAttentionItem(ns.Id, "sig-1") };
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateIncident(ns.Id, "sig-1")));
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>(
                [new ReasoningProposal("not-a-real-ref", "hallucinated", [])]));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        await worker.RunSweepCycleAsync(CancellationToken.None);

        _playbookLedgerMock.Verify(
            p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunSweepCycleAsync_PlaybookLedgerProposeFails_DoesNotThrow()
    {
        var ns = CreateTestNamespace();
        var items = new[] { CreateAttentionItem(ns.Id, "sig-1") };
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([ns]));
        _attentionQueueMock.Setup(a => a.GetAttentionQueueAsync(ns.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AttentionQueueResponse(items, IsEmpty: false)));
        _incidentReadModelMock
            .Setup(i => i.GetIncidentAsync(ns.OwnerId, ns.Id, "sig-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateIncident(ns.Id, "sig-1")));
        var expectedRef = global::ServiceHub.Infrastructure.Agent.ReasoningEvidenceMapper.BuildRef(ns.Id, "sig-1");
        _reasoningAgentClientMock
            .Setup(c => c.ProposeAsync(It.IsAny<IReadOnlyList<ReasoningEvidenceRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ReasoningProposal>>(
                [new ReasoningProposal(expectedRef, "summary", [])]));
        _playbookLedgerMock
            .Setup(p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PlaybookEntry>(Error.Internal("test.error", "boom")));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        var act = () => worker.RunSweepCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── ExecuteAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Disabled_CompletesWithoutCallingAnyDependency()
    {
        var options = EnabledOptions();
        options.Enabled = false;
        var worker = CreateWorker(options, BuildServiceProvider());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        _namespaceRepoMock.Verify(r => r.GetActiveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _namespaceRepoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = CreateWorker(EnabledOptions(), BuildServiceProvider());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
