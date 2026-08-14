using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

/// <summary>
/// Phase D fast-demotion increment (roadmap §7.6, §8.5, §8.6): two consecutive verified
/// <c>Returned</c> outcomes for a signature currently holding L4/L5 standing must demote its
/// <see cref="AutonomyGrant"/> to L3 immediately — on the same scan cycle that records the second
/// <c>Returned</c> — rather than waiting for <c>AutonomyEvaluationWorker</c>'s hourly sweep. Uses
/// the real <see cref="RecoveryLedgerService"/> against an in-memory <see cref="DlqDbContext"/>,
/// same harness as <c>DlqMonitorServiceRecurrenceTests</c>, so these exercise the full
/// detection-to-grant-transition path, including atomicity of the grant row and its paired
/// forensic event.
/// </summary>
public sealed class DlqMonitorServiceFastDemotionTests : IDisposable
{
    private const string EntityName = "orders-dlq";
    private const string SignatureHash = "sig-fast-demotion";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IForensicEngineRouter> _forensicMock = new();
    private readonly Mock<ICloudMessagingProvider> _providerMock = new();
    private readonly Mock<IMessageReceiver> _receiverMock = new();

    private readonly Guid _namespaceId = Guid.NewGuid();
    private readonly Namespace _namespace;

    public DlqMonitorServiceFastDemotionTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);

        _namespace = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(_namespace, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(_namespace));

        _providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Azure);
        _providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Azure);
        _providerMock.Setup(p => p.GetMessageReceiver()).Returns(_receiverMock.Object);
        _providerMock.Setup(p => p.ListEntitiesAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(
                new[] { new CloudEntity { Name = EntityName, EntityType = "Queue", DeadLetterCount = 1, Provider = CloudProviderType.Azure } }));

        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.MaxDelivery, 0.99, "Max delivery", "Safe", "Deterministic"));
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMonitorService CreateSut()
    {
        var router = new CloudProviderRouter(new[] { _providerMock.Object });
        var configuration = new ConfigurationBuilder().Build();
        return new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            configuration, _recoveryLedger, new DlqNotMonitoredLogGuard(),
            NullLogger<DlqMonitorService>.Instance);
    }

    private void SetupPeek(Message message) =>
        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

    private static Message MakeMessage(long sequenceNumber, string marker) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = sequenceNumber,
            Body = "test body",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 1,
            ApplicationProperties = new Dictionary<string, object> { ["x-servicehub-recovery-id"] = marker },
        };

    /// <summary>Opens a Replay operation and begins+accepts an entry, landing it in Observing
    /// with an applied marker so a scan can attribute a recurrence back to it exactly — the state
    /// a real replay leaves behind. <paramref name="signatureHash"/> defaults to the shared
    /// constant so consecutive entries naturally share one signature's trust evidence.</summary>
    private async Task<RecoveryLedgerEntry> BeginObservingEntryAsync(string? signatureHash = SignatureHash)
    {
        var operationResult = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = _namespace.OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            ScopeDescription = $"entity={EntityName}",
            TargetCount = 1,
        });
        operationResult.IsSuccess.Should().BeTrue();

        var beginResult = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationResult.Value.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            EntityNameSnapshot = EntityName,
            BodyHash = $"body-{Guid.NewGuid()}",
            SignatureHashSnapshot = signatureHash,
            TargetEntity = EntityName,
        });
        beginResult.IsSuccess.Should().BeTrue();
        var entry = beginResult.Value;

        var executed = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = entry.Id.ToString(),
            MarkerApplied = true,
        });
        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Observing);

        return executed.Value;
    }

    /// <summary>Closes an already-Observing entry as Recovered directly through the ledger
    /// (bypassing a scan) — used to interleave a "did not return" outcome between two scans.</summary>
    private async Task CloseAsRecoveredAsync(RecoveryLedgerEntry entry)
    {
        var result = await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });
        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>Closes an already-Observing entry as Unverified directly through the ledger.</summary>
    private async Task CloseAsUnverifiedAsync(RecoveryLedgerEntry entry)
    {
        var result = await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryObservationOutcome.ObservationUnavailable,
        });
        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>Scans a message carrying <paramref name="entry"/>'s marker, driving it to
    /// Returned via <see cref="DlqMonitorService.ScanNamespaceAsync"/> — the only code path that
    /// can ever trigger fast-demotion.</summary>
    private async Task ScanRecurrenceAsync(RecoveryLedgerEntry entry)
    {
        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: Random.Shared.NextInt64(1, 100_000), marker: entry.Id.ToString()));
        await sut.ScanNamespaceAsync(_namespaceId);
    }

    private async Task<AutonomyGrant> GrantAsync(AutonomyLevel level, string? signatureHash = SignatureHash)
    {
        var previous = level switch
        {
            AutonomyLevel.Standing => AutonomyLevel.Approve,
            AutonomyLevel.Unattended => AutonomyLevel.Standing,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        if (level == AutonomyLevel.Unattended)
        {
            await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
                _namespace.OwnerId, signatureHash!, RecoveryOperationKind.Replay,
                AutonomyLevel.Approve, AutonomyLevel.Standing, "test setup: reach L4 first", null);
        }

        var result = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            _namespace.OwnerId, signatureHash!, RecoveryOperationKind.Replay,
            previous, level, "test setup", null);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private Task<AutonomyGrant?> ReadGrantAsync(string? signatureHash = SignatureHash) =>
        _recoveryLedger.GetAutonomyGrantAsync(_namespace.OwnerId, signatureHash!, RecoveryOperationKind.Replay);

    // 1. First Returned → no fast demotion.
    [Fact]
    public async Task SingleReturned_AtL4_DoesNotDemote()
    {
        await GrantAsync(AutonomyLevel.Standing);
        var entry = await BeginObservingEntryAsync();

        await ScanRecurrenceAsync(entry);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Standing);
    }

    // 2. Two consecutive Returned → L4 → L3.
    [Fact]
    public async Task TwoConsecutiveReturned_AtL4_DemotesToL3()
    {
        await GrantAsync(AutonomyLevel.Standing);
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(second);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Approve);
    }

    // 3. Two consecutive Returned → L5 → L3 (never an intermediate L4 step).
    [Fact]
    public async Task TwoConsecutiveReturned_AtL5_DemotesDirectlyToL3()
    {
        await GrantAsync(AutonomyLevel.Unattended);
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(second);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Approve, "L5's fast demotion skips L4 entirely (roadmap §8.6)");
    }

    // 4. Returned → Recovered → Returned → no fast demotion.
    [Fact]
    public async Task ReturnedThenRecoveredThenReturned_AtL4_DoesNotDemote()
    {
        await GrantAsync(AutonomyLevel.Standing);
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await CloseAsRecoveredAsync(second);

        var third = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(third);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Standing, "a Recovered outcome in between breaks the consecutive streak");
    }

    // 5. Returned → Unverified → Returned → demotes (Unverified carries no evidence, per §8.10/§14).
    [Fact]
    public async Task ReturnedThenUnverifiedThenReturned_AtL4_StillDemotes()
    {
        await GrantAsync(AutonomyLevel.Standing);
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await CloseAsUnverifiedAsync(second);

        var third = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(third);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(
            AutonomyLevel.Approve,
            "Unverified is excluded from the evidence population entirely (§8.10/§14) — it must not break the streak");
    }

    // 6. ExecutionFailed does not incorrectly count as Returned.
    [Fact]
    public async Task ExecutionFailedThenReturned_AtL4_DoesNotDemote()
    {
        await GrantAsync(AutonomyLevel.Standing);

        var rejectedOperation = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = _namespace.OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            ScopeDescription = $"entity={EntityName}",
            TargetCount = 1,
        });
        var rejectedEntry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = rejectedOperation.Value.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            EntityNameSnapshot = EntityName,
            BodyHash = $"body-{Guid.NewGuid()}",
            SignatureHashSnapshot = SignatureHash,
            TargetEntity = EntityName,
        });
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = rejectedEntry.Value.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        var second = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(second);

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Standing, "ExecutionFailed is never part of the Returned-streak evidence");
    }

    // 10. Null SignatureHash is safely ignored.
    [Fact]
    public async Task NullSignatureHash_SafelyIgnored_NoException()
    {
        var first = await BeginObservingEntryAsync(signatureHash: null);
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync(signatureHash: null);
        var act = async () => await ScanRecurrenceAsync(second);

        await act.Should().NotThrowAsync("a null SignatureHashSnapshot has no per-signature trust identity to demote (roadmap §4)");
    }

    // 11. Existing L3 / no grant is a no-op.
    [Fact]
    public async Task TwoConsecutiveReturned_NoGrantEverCreated_RemainsNoGrant()
    {
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(second);

        var grant = await ReadGrantAsync();
        grant.Should().BeNull("a signature that never earned L4/L5 has nothing to fast-demote");
    }

    // 14. Forensic AutonomyGrantDemoted event is paired with the grant transition (atomicity).
    [Fact]
    public async Task TwoConsecutiveReturned_WritesPairedForensicEvent()
    {
        await GrantAsync(AutonomyLevel.Standing);
        var first = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(first);

        var second = await BeginObservingEntryAsync();
        await ScanRecurrenceAsync(second);

        var demotedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.OwnerId == _namespace.OwnerId && e.EventType == RecoveryEventType.AutonomyGrantDemoted)
            .ToListAsync();

        demotedEvents.Should().ContainSingle();
        demotedEvents[0].DetailJson.Should().Contain(SignatureHash).And.Contain("Standing").And.Contain("Approve");

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Approve);
    }

    // 17. Ordering uses the authoritative ledger ordering mechanism, not scan/wall-clock order.
    [Fact]
    public async Task ThirdEntryClosedOutOfBegunOrder_StillEvaluatesByClosureOrder()
    {
        await GrantAsync(AutonomyLevel.Standing);

        // Begin three entries; close them out of begin-order — the second-begun entry closes
        // last (as the second Returned), so the fast-demotion check must react to closure order,
        // not the order the entries were opened in.
        var entryA = await BeginObservingEntryAsync(); // begins 1st
        var entryB = await BeginObservingEntryAsync(); // begins 2nd

        await ScanRecurrenceAsync(entryA); // closes 1st (1st Returned)

        var stillStanding = await ReadGrantAsync();
        stillStanding!.CurrentLevel.Should().Be(AutonomyLevel.Standing);

        await ScanRecurrenceAsync(entryB); // closes 2nd (2nd Returned) — demotes

        var grant = await ReadGrantAsync();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Approve);
    }
}
