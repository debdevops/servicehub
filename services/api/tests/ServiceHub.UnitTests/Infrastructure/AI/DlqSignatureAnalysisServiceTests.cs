using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class DlqSignatureAnalysisServiceTests
{
    private const string OwnerId = "entra:test-owner-123";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private readonly Mock<IDlqHistoryService> _historyService = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<INamespaceSignatureLookupService> _signatureLookupService = new();
    private readonly DlqSignatureAnalysisService _sut;

    public DlqSignatureAnalysisServiceTests()
    {
        _sut = new DlqSignatureAnalysisService(
            _historyService.Object,
            _aiServiceClient.Object,
            _signatureLookupService.Object);
    }

    private static DlqMessage CreateMessage(long id, DateTimeOffset detectedAt, DateTimeOffset? deadLetterAt = null)
    {
        var message = new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = $"hash-{id}",
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            EntityName = "orders-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = detectedAt.AddMinutes(-5),
            DeadLetterTimeUtc = deadLetterAt,
            DetectedAtUtc = detectedAt,
            DeadLetterReason = "MaxDeliveryCountExceeded",
        };

        typeof(DlqMessage).GetProperty(nameof(DlqMessage.Id))!.SetValue(message, id);
        return message;
    }

    private void SetupMessages(params DlqMessage[] messages)
    {
        _historyService.Setup(s => s.ExportAsync(
            OwnerId, NamespaceId, null, null, null, DlqMessageStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Success(messages));
    }

    private void SetupLookupPassthrough()
    {
        _signatureLookupService
            .Setup(s => s.LookupAndRecordAsync(
                OwnerId, NamespaceId, It.IsAny<IReadOnlyList<ClusterSignatureObservation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid _, IReadOnlyList<ClusterSignatureObservation> obs, CancellationToken _) =>
                obs.ToDictionary(
                    o => o.SignatureHash,
                    o => new SignatureLookupResult(IsNew: true, FirstSeenAt: DateTimeOffset.UtcNow, OccurrenceCount: 1)));
    }

    // ── Export/AI degradation ──────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ExportFails_ReturnsFailure()
    {
        _historyService.Setup(s => s.ExportAsync(
            OwnerId, NamespaceId, null, null, null, DlqMessageStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Failure(Error.Internal("DB_ERROR", "boom")));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.IsFailure.Should().BeTrue();
        _aiServiceClient.Verify(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnalyzeAsync_NoMessages_ReturnsAvailableEmptyResultWithoutCallingAI()
    {
        SetupMessages();

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Available.Should().BeTrue();
        result.Value.Clusters.Should().BeEmpty();
        result.Value.Singletons.Should().BeEmpty();
        _aiServiceClient.Verify(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnalyzeAsync_AIUnavailable_ReturnsAvailableFalseWithoutThrowing()
    {
        SetupMessages(CreateMessage(1, DateTimeOffset.UtcNow));
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ClusterAnalysisResult>(Error.Internal("AI_UNAVAILABLE", "down")));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Available.Should().BeFalse();
        result.Value.Method.Should().BeNull();
        result.Value.Clusters.Should().BeEmpty();
        result.Value.Singletons.Should().BeEmpty();
        _signatureLookupService.Verify(s => s.LookupAndRecordAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ClusterSignatureObservation>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Composition ─────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MapsMemberRefsToCorrectMessageIds()
    {
        var msg1 = CreateMessage(101, DateTimeOffset.UtcNow.AddHours(-2));
        var msg2 = CreateMessage(102, DateTimeOffset.UtcNow.AddHours(-1));
        SetupMessages(msg1, msg2);
        SetupLookupPassthrough();

        var cluster = new ClusterSummary(
            Size: 2,
            RepresentativeRef: "0",
            TopTerms: ["timeout"],
            FirstOccurrenceRef: "0",
            LastOccurrenceRef: "1",
            DominantEntity: "orders-queue",
            DominantDeadletterReason: "MaxDeliveryCountExceeded",
            MemberRefs: ["0", "1"],
            DominantDeadletterReasonCount: 2);

        var analysis = new ClusterAnalysisResult(
            Clusters: [cluster],
            Singletons: [],
            Method: "clustered",
            RefToMessageId: new Dictionary<string, long> { ["0"] = 101, ["1"] = 102 });

        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.Value.Clusters.Should().HaveCount(1);
        result.Value.Clusters[0].MessageIds.Should().BeEquivalentTo(new long[] { 101, 102 });
    }

    [Fact]
    public async Task AnalyzeAsync_ComputesSignatureHashPerClusterAndCallsLookupOnceForBatch()
    {
        var msg1 = CreateMessage(1, DateTimeOffset.UtcNow.AddHours(-2));
        var msg2 = CreateMessage(2, DateTimeOffset.UtcNow.AddHours(-1));
        SetupMessages(msg1, msg2);

        var clusterA = new ClusterSummary(1, "0", ["term-a"], "0", "0", "queue-a", "MaxDeliveryCountExceeded", ["0"], 1);
        var clusterB = new ClusterSummary(1, "1", ["term-b"], "1", "1", "queue-b", "TTLExpiredException", ["1"], 1);

        var analysis = new ClusterAnalysisResult(
            Clusters: [clusterA, clusterB],
            Singletons: [],
            Method: "clustered",
            RefToMessageId: new Dictionary<string, long> { ["0"] = 1, ["1"] = 2 });

        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        IReadOnlyList<ClusterSignatureObservation>? capturedObservations = null;
        _signatureLookupService
            .Setup(s => s.LookupAndRecordAsync(OwnerId, NamespaceId, It.IsAny<IReadOnlyList<ClusterSignatureObservation>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, IReadOnlyList<ClusterSignatureObservation>, CancellationToken>((_, _, obs, _) => capturedObservations = obs)
            .ReturnsAsync((string _, Guid _, IReadOnlyList<ClusterSignatureObservation> obs, CancellationToken _) =>
                obs.ToDictionary(o => o.SignatureHash, o => new SignatureLookupResult(true, DateTimeOffset.UtcNow, 1)));

        await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        _signatureLookupService.Verify(s => s.LookupAndRecordAsync(
            OwnerId, NamespaceId, It.IsAny<IReadOnlyList<ClusterSignatureObservation>>(), It.IsAny<CancellationToken>()), Times.Once);

        capturedObservations.Should().HaveCount(2);
        capturedObservations![0].SignatureHash.Should().Be(ClusterSignatureHasher.ComputeHash(clusterA.TopTerms, clusterA.DominantDeadletterReason));
        capturedObservations![1].SignatureHash.Should().Be(ClusterSignatureHasher.ComputeHash(clusterB.TopTerms, clusterB.DominantDeadletterReason));
    }

    [Fact]
    public async Task AnalyzeAsync_SurfacesIsNewFirstSeenAtOccurrenceCountPerCluster()
    {
        var msg = CreateMessage(1, DateTimeOffset.UtcNow);
        SetupMessages(msg);

        var cluster = new ClusterSummary(1, "0", ["term"], "0", "0", "queue-a", "MaxDeliveryCountExceeded", ["0"], 1);
        var analysis = new ClusterAnalysisResult([cluster], [], "clustered", new Dictionary<string, long> { ["0"] = 1 });
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var firstSeen = DateTimeOffset.UtcNow.AddDays(-3);
        var hash = ClusterSignatureHasher.ComputeHash(cluster.TopTerms, cluster.DominantDeadletterReason);
        _signatureLookupService
            .Setup(s => s.LookupAndRecordAsync(OwnerId, NamespaceId, It.IsAny<IReadOnlyList<ClusterSignatureObservation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SignatureLookupResult>
            {
                [hash] = new SignatureLookupResult(IsNew: false, FirstSeenAt: firstSeen, OccurrenceCount: 7)
            });

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        var signature = result.Value.Clusters.Single();
        signature.IsNew.Should().BeFalse();
        signature.FirstSeenAt.Should().Be(firstSeen);
        signature.OccurrenceCount.Should().Be(7);
    }

    [Fact]
    public async Task AnalyzeAsync_ExplanationIsNonEmptyForEveryCluster()
    {
        var msg1 = CreateMessage(1, DateTimeOffset.UtcNow.AddHours(-2));
        var msg2 = CreateMessage(2, DateTimeOffset.UtcNow.AddHours(-1));
        SetupMessages(msg1, msg2);
        SetupLookupPassthrough();

        var clusterA = new ClusterSummary(1, "0", ["term-a"], "0", "0", "queue-a", "MaxDeliveryCountExceeded", ["0"], 1);
        var clusterB = new ClusterSummary(1, "1", ["term-b"], "1", "1", "queue-b", "TTLExpiredException", ["1"], 1);
        var analysis = new ClusterAnalysisResult(
            [clusterA, clusterB], [], "clustered", new Dictionary<string, long> { ["0"] = 1, ["1"] = 2 });
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.Value.Clusters.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Explanation));
    }

    [Fact]
    public async Task AnalyzeAsync_WindowDerivedFromRealMessageTimestamps()
    {
        var earlier = DateTimeOffset.UtcNow.AddDays(-2);
        var later = DateTimeOffset.UtcNow.AddHours(-1);
        // FirstOccurrenceRef intentionally points at the chronologically later message,
        // to prove the window isn't derived from ref ordering.
        var msgEarly = CreateMessage(1, earlier, deadLetterAt: earlier);
        var msgLate = CreateMessage(2, later, deadLetterAt: later);
        SetupMessages(msgEarly, msgLate);
        SetupLookupPassthrough();

        var cluster = new ClusterSummary(
            2, "0", ["term"], FirstOccurrenceRef: "1", LastOccurrenceRef: "0",
            DominantEntity: "queue-a", DominantDeadletterReason: "MaxDeliveryCountExceeded",
            MemberRefs: ["0", "1"], DominantDeadletterReasonCount: 2);

        var analysis = new ClusterAnalysisResult(
            [cluster], [], "clustered", new Dictionary<string, long> { ["0"] = 1, ["1"] = 2 });
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        var signature = result.Value.Clusters.Single();
        signature.WindowStart.Should().Be(earlier);
        signature.WindowEnd.Should().Be(later);
    }

    [Fact]
    public async Task AnalyzeAsync_SingletonsPreservedAsDistinctGroupNotFoldedIntoClusters()
    {
        var msg1 = CreateMessage(1, DateTimeOffset.UtcNow.AddHours(-2));
        var msg2 = CreateMessage(2, DateTimeOffset.UtcNow.AddHours(-1));
        SetupMessages(msg1, msg2);
        SetupLookupPassthrough();

        var cluster = new ClusterSummary(1, "0", ["term"], "0", "0", "queue-a", "MaxDeliveryCountExceeded", ["0"], 1);
        var singleton = new ClusterSingleton(Ref: "1", DominantEntity: "queue-b", DominantDeadletterReason: "TTLExpiredException");

        var analysis = new ClusterAnalysisResult(
            [cluster], [singleton], "clustered", new Dictionary<string, long> { ["0"] = 1, ["1"] = 2 });
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.Value.Clusters.Should().HaveCount(1);
        result.Value.Singletons.Should().HaveCount(1);
        result.Value.Singletons[0].MessageId.Should().Be(2);
        result.Value.Singletons[0].DominantEntity.Should().Be("queue-b");
    }

    [Theory]
    [InlineData("clustered")]
    [InlineData("grouped")]
    public async Task AnalyzeAsync_PassesMethodThroughUnchanged(string method)
    {
        var msg = CreateMessage(1, DateTimeOffset.UtcNow);
        SetupMessages(msg);
        SetupLookupPassthrough();

        var cluster = new ClusterSummary(1, "0", ["term"], "0", "0", "queue-a", "MaxDeliveryCountExceeded", ["0"], 1);
        var analysis = new ClusterAnalysisResult([cluster], [], method, new Dictionary<string, long> { ["0"] = 1 });
        _aiServiceClient.Setup(c => c.AnalyzeMessagesAsync(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(analysis));

        var result = await _sut.AnalyzeAsync(OwnerId, NamespaceId);

        result.Value.Method.Should().Be(method);
    }

    // ── Constructor ─────────────────────────────────────────

    [Fact]
    public void Constructor_NullHistoryService_Throws()
    {
        var act = () => new DlqSignatureAnalysisService(null!, _aiServiceClient.Object, _signatureLookupService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("historyService");
    }

    [Fact]
    public void Constructor_NullAIServiceClient_Throws()
    {
        var act = () => new DlqSignatureAnalysisService(_historyService.Object, null!, _signatureLookupService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("aiServiceClient");
    }

    [Fact]
    public void Constructor_NullSignatureLookupService_Throws()
    {
        var act = () => new DlqSignatureAnalysisService(_historyService.Object, _aiServiceClient.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("signatureLookupService");
    }
}
