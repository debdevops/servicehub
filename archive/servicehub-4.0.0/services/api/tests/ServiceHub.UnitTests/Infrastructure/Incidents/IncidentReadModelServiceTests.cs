using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Incidents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Incidents;

public sealed class IncidentReadModelServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";
    private const string SignatureHash = "sig-abc123";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private readonly DlqDbContext _dbContext;
    private readonly Mock<ISignatureLifecycleService> _lifecycleMock = new();
    private readonly Mock<IRecoveryLedger> _recoveryLedgerMock = new();
    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<INamespaceSignatureLookupService> _signatureLookupServiceMock = new();
    private readonly IncidentReadModelService _service;

    public IncidentReadModelServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new IncidentReadModelService(
            _dbContext, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object, _signatureLookupServiceMock.Object);

        SetupNoRecoveryEntries();
        SetupNoPlaybookEntries();
        SetupDefaultLifecycle();
        SetupNamespaceLookupFails();
        SetupSignatureLookupFindsNothing();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private void SetupNoRecoveryEntries() => SetupRecoveryEntries();

    private void SetupRecoveryEntries(params RecoveryLedgerEntry[] entries) =>
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, SignatureHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RecoveryLedgerEntry>)entries);

    private void SetupNoPlaybookEntries() => SetupPlaybookEntries();

    private void SetupPlaybookEntries(params PlaybookEntry[] entries) =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, NamespaceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(entries));

    private void SetupPlaybookQueryFails() =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, NamespaceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<PlaybookEntry>>(Error.Internal("ERR", "boom")));

    private void SetupDefaultLifecycle() =>
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, SignatureHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Active, null, null, null)));

    private void SetupNamespaceLookupFails() =>
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("Namespace.NotFound", "not found")));

    private void SetupSignatureLookupFindsNothing() =>
        _signatureLookupServiceMock
            .Setup(s => s.GetByHashAsync(OwnerId, NamespaceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NamespaceSignature?)null);

    private async Task SeedNamespaceSignatureAsync()
    {
        _dbContext.NamespaceSignatures.Add(new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = SignatureHash,
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastSeenAt = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            OccurrenceCount = 4,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = """["timeout","sql"]""",
        });
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>Seeds a Fingerprint <see cref="NamespaceSignature"/> at <see cref="SignatureHash"/>
    /// whose top terms carry a real <c>entity:</c> term — needed for the entity-matching tests
    /// below, unlike <see cref="SeedNamespaceSignatureAsync"/>'s generic terms.</summary>
    private async Task SeedNamespaceSignatureWithEntityAsync(string entity)
    {
        _dbContext.NamespaceSignatures.Add(new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = SignatureHash,
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 4,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = $"""["entity:{entity}","reason:TestingDLQ"]""",
        });
        await _dbContext.SaveChangesAsync();
    }

    private static RecoveryLedgerEntry BuildRecoveryEntry(RecoveryEntryState state) => new()
    {
        OperationId = Guid.NewGuid(),
        OwnerId = OwnerId,
        BodyHash = "body-hash",
        TargetEntity = "orders-dlq",
        BegunAt = DateTimeOffset.UtcNow,
        SignatureHashSnapshot = SignatureHash,
        State = state,
    };

    private static PlaybookEntry BuildPlaybookEntry(
        string proposalKind, PlaybookEntryState state, string? signatureHash = SignatureHash) => new()
    {
        OwnerId = OwnerId,
        PillarKind = PillarKind.Investigate,
        ProposalKind = proposalKind,
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:Test",
        ProposerKind = PlaybookActorKind.System,
        SignatureHashSnapshot = signatureHash,
        NamespaceId = NamespaceId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        State = state,
    };

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new IncidentReadModelService(
            null!, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object, _signatureLookupServiceMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullLifecycleService_Throws()
    {
        var act = () => new IncidentReadModelService(
            _dbContext, null!, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object, _signatureLookupServiceMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("lifecycle");
    }

    [Fact]
    public void Constructor_NullSignatureLookupService_Throws()
    {
        var act = () => new IncidentReadModelService(
            _dbContext, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("signatureLookupService");
    }

    [Fact]
    public async Task GetIncidentAsync_NothingRecordedAnywhere_ReturnsNotFound()
    {
        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetIncidentAsync_EmptySignatureHash_ReturnsNotFound_WithoutQueryingLedgers()
    {
        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, string.Empty);

        result.IsFailure.Should().BeTrue();
        _recoveryLedgerMock.Verify(
            r => r.FindEntriesForSignatureSinceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _playbookLedgerMock.Verify(
            l => l.QueryEntriesAsync(
                It.IsAny<string>(), It.IsAny<PillarKind?>(), It.IsAny<Guid?>(), It.IsAny<PlaybookEntryState?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetIncidentAsync_OnlyNamespaceSignatureRecorded_BuildsFromSignatureFields()
    {
        await SeedNamespaceSignatureAsync();

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        var incident = result.Value;
        incident.OccurrenceCount.Should().Be(4);
        incident.DominantDeadletterReason.Should().Be("MaxDeliveryCountExceeded");
        incident.TopTerms.Should().BeEquivalentTo(new[] { "timeout", "sql" });
        incident.RecoveryEntries.Should().BeEmpty();
        incident.PlaybookEntries.Should().BeEmpty();
        incident.Summary.RecoveryEntryCount.Should().Be(0);
    }

    [Fact]
    public async Task GetIncidentAsync_OnlyRecoveryActivity_NoSignatureRow_StillFound()
    {
        // A signature can have recovery/playbook activity recorded against it before (or without)
        // ever being persisted as a NamespaceSignature row (e.g. a manual single-message replay
        // outside a scan) — this must still resolve, not 404.
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.RecoveryEntries.Should().ContainSingle();
        result.Value.OccurrenceCount.Should().Be(0);
    }

    [Fact]
    public async Task GetIncidentAsync_PlaybookEntries_FiltersToMatchingSignatureOnly()
    {
        SetupPlaybookEntries(
            BuildPlaybookEntry("AnomalyFlag", PlaybookEntryState.Proposed, signatureHash: SignatureHash),
            BuildPlaybookEntry("DriftFinding", PlaybookEntryState.Proposed, signatureHash: "some-other-sig"),
            BuildPlaybookEntry("CorrelationHypothesis", PlaybookEntryState.Proposed, signatureHash: null));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().ContainSingle().Which.ProposalKind.Should().Be("AnomalyFlag");
    }

    [Fact]
    public async Task GetIncidentAsync_SummaryCounts_OpenAndPendingRecoveryStates()
    {
        SetupRecoveryEntries(
            BuildRecoveryEntry(RecoveryEntryState.Executing),
            BuildRecoveryEntry(RecoveryEntryState.Observing),
            BuildRecoveryEntry(RecoveryEntryState.Declined),
            BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.Summary.RecoveryEntryCount.Should().Be(4);
        result.Value.Summary.OpenRecoveryEntryCount.Should().Be(2);
        result.Value.Summary.PendingDecisionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetIncidentAsync_SummaryCounts_PendingPlaybookStatesAndProposalKinds()
    {
        SetupPlaybookEntries(
            BuildPlaybookEntry("AnomalyFlag", PlaybookEntryState.Proposed),
            BuildPlaybookEntry("DriftFinding", PlaybookEntryState.UnderReview),
            BuildPlaybookEntry("CorrelationHypothesis", PlaybookEntryState.Approved),
            BuildPlaybookEntry("PreventionTrigger", PlaybookEntryState.Expired),
            BuildPlaybookEntry("ReplayPlan", PlaybookEntryState.Rejected));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        var summary = result.Value.Summary;
        summary.AnomalyFlagCount.Should().Be(1);
        summary.DriftFindingCount.Should().Be(1);
        summary.CorrelationHypothesisCount.Should().Be(1);
        summary.PreventionTriggerCount.Should().Be(1);
        summary.ReplayPlanCount.Should().Be(1);
        // Only the Proposed + UnderReview entries are pending a decision.
        summary.PendingDecisionCount.Should().Be(2);
    }

    [Fact]
    public async Task GetIncidentAsync_PlaybookQueryFails_TreatedAsEmptyRatherThanThrowing()
    {
        SetupPlaybookQueryFails();
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIncidentAsync_NamespaceLookupFails_FallsBackToSnapshotName()
    {
        SetupRecoveryEntries(new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerId,
            BodyHash = "body-hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
            SignatureHashSnapshot = SignatureHash,
            NamespaceNameSnapshot = "prod-orders",
            State = RecoveryEntryState.Recovered,
        });

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.NamespaceName.Should().Be("prod-orders");
    }

    [Fact]
    public async Task GetIncidentAsync_ClusterHashWithUniqueFingerprintSibling_ResolvesToSiblingIncident()
    {
        // Mirrors the real bug: a replay launched from a Failure Signature Detail page's Cluster
        // hash writes its RecoveryLedgerEntries under a sibling Fingerprint hash (by design, see
        // SignatureReplayExecutor.ResolveTrustSignatureHashAsync); the "check verification status"
        // link still points at the Cluster hash, so a naive exact-match lookup 404s despite real
        // recovery activity existing under the sibling Fingerprint hash.
        const string clusterHash = "cluster-hash-xyz";
        const string fingerprintHash = "fingerprint-hash-xyz";

        var clusterSignature = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = clusterHash,
            HashKind = SignatureHashKind.Cluster,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 44,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        var fingerprintSibling = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = fingerprintHash,
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 38,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };

        _dbContext.NamespaceSignatures.Add(fingerprintSibling);
        await _dbContext.SaveChangesAsync();

        _signatureLookupServiceMock
            .Setup(s => s.GetByHashAsync(OwnerId, NamespaceId, clusterHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clusterSignature);
        _signatureLookupServiceMock
            .Setup(s => s.GetAllForNamespaceAsync(
                OwnerId, NamespaceId, SignatureHashKind.Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { fingerprintSibling });

        var recoveryEntry = new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerId,
            BodyHash = "body-hash",
            TargetEntity = "orders",
            BegunAt = DateTimeOffset.UtcNow,
            SignatureHashSnapshot = fingerprintHash,
            State = RecoveryEntryState.Observing,
        };
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, clusterHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecoveryLedgerEntry>());
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, fingerprintHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { recoveryEntry });
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, fingerprintHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Active, null, null, null)));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, clusterHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.SignatureHash.Should().Be(fingerprintHash);
        result.Value.RecoveryEntries.Should().ContainSingle();
        result.Value.OccurrenceCount.Should().Be(38);
    }

    [Fact]
    public async Task GetIncidentAsync_ClusterHashWithAmbiguousSiblings_ButOwnReplayJobExists_ResolvesPrecisely()
    {
        // The real production case (found 2026-09-14): a namespace accumulates several
        // Fingerprint signatures that all collide on entity+reason (repeated manual Test DLQ
        // batches against the same queue), so the entity+reason heuristic alone can't tell them
        // apart. But when the Cluster hash itself has a completed SignatureReplayJob, we know
        // exactly which DlqMessage rows it replayed — tracing those to their RecoveryLedgerEntries
        // resolves the correct sibling without guessing, even with 3 ambiguous candidates present.
        const string clusterHash = "cluster-hash-precise";
        const string correctSibling = "fp-correct";
        const long replayedMessageId1 = 101;
        const long replayedMessageId2 = 102;

        var sibling1 = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = "fp-other-1",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        var sibling2 = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = "fp-other-2",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        var sibling3Correct = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = correctSibling,
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 22,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        _dbContext.NamespaceSignatures.AddRange(sibling3Correct);
        _dbContext.SignatureReplayJobs.Add(new SignatureReplayJob
        {
            OwnerId = OwnerId,
            RequestedByIdentity = "user@example.com",
            RequestedByActorKind = RecoveryActorKind.User,
            NamespaceId = NamespaceId,
            NamespaceDisplayName = "Azure DEV",
            SignatureHash = clusterHash,
            MessageIdsJson = $"[{replayedMessageId1},{replayedMessageId2}]",
            TotalMatched = 2,
            ProcessedCount = 2,
            SuccessCount = 2,
            Status = BulkOperationStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        _dbContext.RecoveryLedgerEntries.AddRange(
            new RecoveryLedgerEntry
            {
                OperationId = Guid.NewGuid(),
                OwnerId = OwnerId,
                BodyHash = "body-hash-1",
                TargetEntity = "orders",
                DlqMessageId = replayedMessageId1,
                BegunAt = DateTimeOffset.UtcNow,
                SignatureHashSnapshot = correctSibling,
                State = RecoveryEntryState.Observing,
            },
            new RecoveryLedgerEntry
            {
                OperationId = Guid.NewGuid(),
                OwnerId = OwnerId,
                BodyHash = "body-hash-2",
                TargetEntity = "orders",
                DlqMessageId = replayedMessageId2,
                BegunAt = DateTimeOffset.UtcNow,
                SignatureHashSnapshot = correctSibling,
                State = RecoveryEntryState.Observing,
            });
        await _dbContext.SaveChangesAsync();

        _signatureLookupServiceMock
            .Setup(s => s.GetAllForNamespaceAsync(
                OwnerId, NamespaceId, SignatureHashKind.Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sibling1, sibling2, sibling3Correct });
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, clusterHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecoveryLedgerEntry>());
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, correctSibling, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RecoveryLedgerEntry
                {
                    OperationId = Guid.NewGuid(), OwnerId = OwnerId, BodyHash = "body-hash-1",
                    TargetEntity = "orders", SignatureHashSnapshot = correctSibling,
                    BegunAt = DateTimeOffset.UtcNow, State = RecoveryEntryState.Observing,
                },
            });
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, correctSibling, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Active, null, null, null)));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, clusterHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.SignatureHash.Should().Be(correctSibling);
        result.Value.OccurrenceCount.Should().Be(22);
        // The entity+reason heuristic was never even consulted — resolution came from the
        // precise message-id trace, so GetByHashAsync (its entry point) was never called.
        _signatureLookupServiceMock.Verify(
            s => s.GetByHashAsync(OwnerId, NamespaceId, clusterHash, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetIncidentAsync_ClusterHashWithAmbiguousFingerprintSiblings_StaysNotFound()
    {
        const string clusterHash = "cluster-hash-ambiguous";

        var clusterSignature = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = clusterHash,
            HashKind = SignatureHashKind.Cluster,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 44,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        var sibling1 = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = "fp-1",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };
        var sibling2 = new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = "fp-2",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DominantDeadletterReason = "TestingDLQ",
            TopTermsJson = """["entity:orders","reason:TestingDLQ"]""",
        };

        _signatureLookupServiceMock
            .Setup(s => s.GetByHashAsync(OwnerId, NamespaceId, clusterHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clusterSignature);
        _signatureLookupServiceMock
            .Setup(s => s.GetAllForNamespaceAsync(
                OwnerId, NamespaceId, SignatureHashKind.Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sibling1, sibling2 });
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, clusterHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecoveryLedgerEntry>());

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, clusterHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetIncidentAsync_AnomalyFlagWithMatchingEntity_LinkedDespiteNullSignatureHash()
    {
        // AnomalyDetectionWorker.ProposePlaybookEntryAsync never sets SignatureHashSnapshot (it's
        // namespace+entity scoped by design) — the Evidence tab must still surface it against the
        // signature sharing its entity, resolved from the persisted NamespaceSignature's own
        // "entity:" top term.
        await SeedNamespaceSignatureWithEntityAsync("timeout");
        var anomaly = new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Investigate,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = "{}",
            ProposalJson = """{"EntityName":"timeout","Type":"VolumeSpike","Severity":3,"Description":"DLQ growth spike on timeout"}""",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "System:AnomalyDetectionWorker",
            ProposerKind = PlaybookActorKind.System,
            SignatureHashSnapshot = null,
            NamespaceId = NamespaceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            State = PlaybookEntryState.Proposed,
        };
        SetupPlaybookEntries(anomaly);

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().ContainSingle().Which.ProposalKind.Should().Be("AnomalyFlag");
        result.Value.Summary.AnomalyFlagCount.Should().Be(1);
    }

    [Fact]
    public async Task GetIncidentAsync_AnomalyFlagWithDifferentEntity_NotLinked()
    {
        await SeedNamespaceSignatureWithEntityAsync("timeout");
        var unrelatedAnomaly = new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Investigate,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = "{}",
            ProposalJson = """{"EntityName":"orders","Type":"VolumeSpike","Severity":3,"Description":"unrelated"}""",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "System:AnomalyDetectionWorker",
            ProposerKind = PlaybookActorKind.System,
            SignatureHashSnapshot = null,
            NamespaceId = NamespaceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            State = PlaybookEntryState.Proposed,
        };
        SetupPlaybookEntries(unrelatedAnomaly);

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIncidentAsync_CorrelationHypothesisWithMatchingMember_LinkedByMembersArray()
    {
        await SeedNamespaceSignatureWithEntityAsync("timeout");
        var correlation = new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Correlate,
            ProposalKind = "CorrelationHypothesis",
            EvidenceRefJson = "{}",
            ProposalJson = """{"Providers":["Azure","Aws"],"Members":[{"NamespaceId":"n1","EntityName":"orders","AnomalyType":"VolumeSpike"},{"NamespaceId":"n2","EntityName":"timeout","AnomalyType":"VolumeSpike"}],"Severity":2,"Description":"2 related failures across Azure and Aws"}""",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "System:CorrelationDetectionWorker",
            ProposerKind = PlaybookActorKind.System,
            SignatureHashSnapshot = null,
            NamespaceId = NamespaceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            State = PlaybookEntryState.Proposed,
        };
        SetupPlaybookEntries(correlation);

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().ContainSingle().Which.ProposalKind.Should().Be("CorrelationHypothesis");
        result.Value.Summary.CorrelationHypothesisCount.Should().Be(1);
    }

    [Fact]
    public async Task GetIncidentAsync_PlaybookEntryAlreadyLinkedBySignatureHash_NotDuplicatedByEntityMatch()
    {
        await SeedNamespaceSignatureAsync();
        // A ReplayPlan already carries SignatureHashSnapshot == SignatureHash directly — it must not
        // also get picked up (and duplicated) by the entity-matching pass, which only considers
        // entries with a null SignatureHashSnapshot.
        SetupPlaybookEntries(BuildPlaybookEntry("ReplayPlan", PlaybookEntryState.Proposed));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.PlaybookEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetIncidentAsync_LifecycleStatus_ReflectsServiceResult()
    {
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, SignatureHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Active, DateTimeOffset.UtcNow, null)));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.LifecycleStatus.Should().Be("Resolved");
    }
}
