using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryLedgerServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _service;

    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    public RecoveryLedgerServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new RecoveryLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor(string identity = "test-actor") => new(identity, RecoveryActorKind.User);

    private RecoveryLedgerService BuildService(double? observationWindowHours)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(observationWindowHours is { } hours
                ? new Dictionary<string, string?> { ["RecoveryEvidence:ObservationWindowHours"] = hours.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : new Dictionary<string, string?>())
            .Build();
        return new RecoveryLedgerService(_dbContext, configuration);
    }

    private async Task<RecoveryOperation> OpenOperationAsync(
        string ownerId = OwnerA, RecoveryOperationKind kind = RecoveryOperationKind.Replay, string? reason = null,
        long? sourceRuleId = null, RecoveryLedgerService? service = null)
    {
        var result = await (service ?? _service).OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = kind,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            Reason = reason ?? (kind == RecoveryOperationKind.Purge ? "test purge reason" : null),
            ScopeDescription = "entity=orders-dlq",
            SourceRuleId = sourceRuleId,
            TargetCount = 1,
        });

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(RecoveryOperation operation, RecoveryLedgerService? service = null)
    {
        var result = await (service ?? _service).BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = operation.OwnerId,
            Actor = Actor(),
            BodyHash = "body-hash-1",
            TargetEntity = "orders-dlq",
        });

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<(RecoveryOperation Operation, RecoveryLedgerEntry Entry)> OpenAndBeginAsync(
        string ownerId = OwnerA, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var operation = await OpenOperationAsync(ownerId, kind);
        var entry = await BeginEntryAsync(operation);
        return (operation, entry);
    }

    // ── GetEntryCountsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetEntryCountsAsync_CountsEntriesPerOperation_OmitsOperationsWithNoEntries()
    {
        var (opWithTwo, _) = await OpenAndBeginAsync();
        await BeginEntryAsync(opWithTwo);
        var (opWithOne, _) = await OpenAndBeginAsync();
        var opWithNone = await OpenOperationAsync();

        var counts = await _service.GetEntryCountsAsync(
            [opWithTwo.Id, opWithOne.Id, opWithNone.Id], OwnerA);

        counts[opWithTwo.Id].Should().Be(2);
        counts[opWithOne.Id].Should().Be(1);
        counts.Should().NotContainKey(opWithNone.Id);
    }

    [Fact]
    public async Task GetEntryCountsAsync_DifferentOwnersEntries_NotCountedAcrossOwners()
    {
        var (opOwnerA, _) = await OpenAndBeginAsync(OwnerA);
        var (opOwnerB, _) = await OpenAndBeginAsync(OwnerB);

        var counts = await _service.GetEntryCountsAsync([opOwnerA.Id, opOwnerB.Id], OwnerA);

        counts[opOwnerA.Id].Should().Be(1);
        counts.Should().NotContainKey(opOwnerB.Id);
    }

    [Fact]
    public async Task GetEntryCountsAsync_EmptyOperationIds_ReturnsEmpty()
    {
        var counts = await _service.GetEntryCountsAsync([], OwnerA);

        counts.Should().BeEmpty();
    }

    // ── OpenOperationAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenOperationAsync_Purge_WithoutReason_Fails()
    {
        var result = await _service.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = OwnerA,
            Kind = RecoveryOperationKind.Purge,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            Reason = null,
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task OpenOperationAsync_ValidRequest_AppendsOperationOpenedEvent()
    {
        var operation = await OpenOperationAsync();

        var events = await _dbContext.RecoveryEvents.Where(e => e.OperationId == operation.Id).ToListAsync();
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.OperationOpened);
        events[0].Seq.Should().Be(1);
        events[0].PrevHash.Should().Be(RecoveryHashChain.GenesisHash);
    }

    // ── Configurable observation window (roadmap W1.1) ─────────────────────

    [Fact]
    public async Task RecordExecutionAsync_NoConfigurationSupplied_UsesTwentyFourHourDefault_NoAuditEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.ObservationWindowEndsAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(RecoveryLedgerService.DefaultObservationWindowHours), TimeSpan.FromMinutes(1));

        var events = await _dbContext.RecoveryEvents.Where(e => e.EntryId == entry.Id).ToListAsync();
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.ObservationWindowOpened);
        events.Should().NotContain(e => e.EventType == RecoveryEventType.NonDefaultObservationWindowApplied);
        events.Single(e => e.EventType == RecoveryEventType.ObservationWindowOpened).DetailJson
            .Should().Contain("\"appliedObservationWindowHours\":24");
    }

    [Fact]
    public async Task RecordExecutionAsync_NonDefaultConfiguredWindow_UsesConfiguredValue_AppendsAuditEvent()
    {
        var service = BuildService(observationWindowHours: 2);
        var operation = await OpenOperationAsync(service: service);
        var entry = await BeginEntryAsync(operation, service);

        var executed = await service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.ObservationWindowEndsAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromMinutes(1));

        var events = await _dbContext.RecoveryEvents.Where(e => e.EntryId == entry.Id).ToListAsync();
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.ObservationWindowOpened);
        var auditEvent = events.Should().ContainSingle(e => e.EventType == RecoveryEventType.NonDefaultObservationWindowApplied)
            .Which;
        auditEvent.DetailJson.Should().Contain("\"appliedObservationWindowHours\":2")
            .And.Contain("\"defaultObservationWindowHours\":24");
    }

    [Fact]
    public async Task RecordExecutionAsync_ConfiguredWindowBelowFloor_ClampsToMinimum()
    {
        var service = BuildService(observationWindowHours: 0);
        var operation = await OpenOperationAsync(service: service);
        var entry = await BeginEntryAsync(operation, service);

        var executed = await service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.Value.ObservationWindowEndsAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(0.1), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RecordExecutionAsync_ConfiguredWindowAboveCeiling_ClampsToMaximum()
    {
        var service = BuildService(observationWindowHours: 100_000);
        var operation = await OpenOperationAsync(service: service);
        var entry = await BeginEntryAsync(operation, service);

        var executed = await service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.Value.ObservationWindowEndsAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(720), TimeSpan.FromMinutes(1));
    }

    // ── Full lifecycle happy paths ──────────────────────────────────────────

    [Fact]
    public async Task Lifecycle_ReplayAcceptedThenNoRecurrence_ReachesRecovered()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Observing);
        executed.Value.ObservationWindowEndsAt.Should().NotBeNull();

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Recovered);
        observed.Value.Disposition.Should().Be(RecoveryDisposition.Recovered);
        observed.Value.VerificationResult.Should().Be(VerificationResult.Recovered);
        observed.Value.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Lifecycle_ReplayAcceptedThenRecurrence_ReachesReturned()
    {
        var (_, entry) = await OpenAndBeginAsync();

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = VerificationConfidence.Exact,
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Returned);
        observed.Value.Disposition.Should().Be(RecoveryDisposition.Returned);
        observed.Value.VerificationConfidence.Should().Be(VerificationConfidence.Exact);
    }

    [Fact]
    public async Task Lifecycle_PurgeAccepted_ReachesDiscarded()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Discarded);
        executed.Value.Disposition.Should().Be(RecoveryDisposition.Discarded);
        executed.Value.ClosedAt.Should().NotBeNull();
        executed.Value.ObservationWindowEndsAt.Should().BeNull();
    }

    [Fact]
    public async Task Lifecycle_ExecutionRejected_ReachesExecutionFailed()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.ExecutionFailed);
        executed.Value.Disposition.Should().Be(RecoveryDisposition.Failed);
    }

    [Fact]
    public async Task Lifecycle_ExecutionUnknownThenWrittenOff_ReachesWrittenOff()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Unknown,
        });
        executed.Value.State.Should().Be(RecoveryEntryState.ExecutionUnknown);

        var disposed = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "operator gave up");

        disposed.IsSuccess.Should().BeTrue();
        disposed.Value.State.Should().Be(RecoveryEntryState.WrittenOff);
        disposed.Value.Disposition.Should().Be(RecoveryDisposition.WrittenOff);
    }

    [Fact]
    public async Task Lifecycle_ReplayAcceptedThenObservationUnavailable_ReachesUnverified()
    {
        // Not-observed must never be reported as verified success (§8.4/§8.5) — Unverified is a
        // distinct terminal state from Recovered, not a synonym for it.
        var (_, entry) = await OpenAndBeginAsync();

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.ObservationUnavailable,
            DetailJson = "{\"reason\":\"AWS_NO_ABSENCE_PROOF\"}",
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Unverified);
        observed.Value.Disposition.Should().Be(RecoveryDisposition.Unverified);
        observed.Value.VerificationResult.Should().Be(VerificationResult.Unverified);
        observed.Value.State.Should().NotBe(RecoveryEntryState.Recovered);
    }

    [Fact]
    public async Task RecordObservationAsync_Recurrence_PreservesPriorExecutionEvidence()
    {
        // A message returning to the DLQ must not erase the original ReplayAccepted evidence —
        // BegunAt, RecoveryMarker and MarkerApplied are immutable-in-spirit facts about the
        // original attempt, not overwritten by the later recurrence transition.
        var (_, entry) = await OpenAndBeginAsync();
        var begunAt = entry.BegunAt;

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = entry.Id.ToString(),
            MarkerApplied = true,
        });
        executed.IsSuccess.Should().BeTrue();

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = VerificationConfidence.Exact,
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Returned);
        observed.Value.BegunAt.Should().Be(begunAt);
        observed.Value.RecoveryMarker.Should().Be(entry.Id.ToString());
        observed.Value.MarkerApplied.Should().BeTrue();

        // The full event chain still has both the original acceptance and the later recurrence —
        // neither event was deleted or overwritten by the other.
        var events = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id).OrderBy(e => e.Seq).Select(e => e.EventType).ToListAsync();
        events.Should().Contain(RecoveryEventType.ProviderAccepted);
        events.Should().Contain(RecoveryEventType.RecurrenceObserved);
    }

    // ── Recurrence lookups ────────────────────────────────────────────────────

    [Fact]
    public async Task FindByMarkerAsync_MatchesObservingEntry()
    {
        var (_, entry) = await OpenAndBeginAsync();
        var marker = entry.Id.ToString();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = marker,
            MarkerApplied = true,
        });

        var found = await _service.FindByMarkerAsync(OwnerA, marker);

        found.Should().NotBeNull();
        found!.Id.Should().Be(entry.Id);
    }

    [Fact]
    public async Task FindByMarkerAsync_DifferentOwner_ReturnsNull()
    {
        var (_, entry) = await OpenAndBeginAsync(OwnerA);
        var marker = entry.Id.ToString();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = marker,
            MarkerApplied = true,
        });

        var found = await _service.FindByMarkerAsync(OwnerB, marker);

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByMarkerAsync_EntryAlreadyTerminal_DoesNotMatch()
    {
        // Once an entry has left Observing (e.g. already Returned by an earlier scan), a second
        // scan finding what looks like the same marker again must not re-match it.
        var (_, entry) = await OpenAndBeginAsync();
        var marker = entry.Id.ToString();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = marker,
            MarkerApplied = true,
        });
        await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = VerificationConfidence.Exact,
        });

        var found = await _service.FindByMarkerAsync(OwnerA, marker);

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindHeuristicRecurrenceCandidatesAsync_MultipleOpenEntriesShareBodyHash_ReturnsAll()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;

        async Task<RecoveryLedgerEntry> BeginWithBodyHashAsync(string bodyHash)
        {
            var begin = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = "orders-dlq",
                BodyHash = bodyHash,
                TargetEntity = "orders-dlq",
            });
            begin.IsSuccess.Should().BeTrue();

            await _service.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = begin.Value.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                Outcome = RecoveryExecutionOutcome.Accepted,
                MarkerApplied = false, // marker could not be applied — heuristic fallback applies
            });

            return begin.Value;
        }

        var first = await BeginWithBodyHashAsync("shared-hash");
        var second = await BeginWithBodyHashAsync("shared-hash");
        await BeginWithBodyHashAsync("different-hash");

        var candidates = await _service.FindHeuristicRecurrenceCandidatesAsync(
            OwnerA, operation.NamespaceId, "orders-dlq", "shared-hash", now.AddMinutes(1));

        candidates.Should().HaveCount(2);
        candidates.Select(c => c.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task FindHeuristicRecurrenceCandidatesAsync_MarkerApplied_ExcludedFromHeuristicMatch()
    {
        // A marker-carrying entry has an exact-match path available (FindByMarkerAsync) and must
        // not also surface as a heuristic candidate — that would double-count the same recurrence.
        var operation = await OpenOperationAsync();
        var begin = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            NamespaceId = operation.NamespaceId,
            EntityNameSnapshot = "orders-dlq",
            BodyHash = "shared-hash",
            TargetEntity = "orders-dlq",
        });
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = begin.Value.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = begin.Value.Id.ToString(),
            MarkerApplied = true,
        });

        var candidates = await _service.FindHeuristicRecurrenceCandidatesAsync(
            OwnerA, operation.NamespaceId, "orders-dlq", "shared-hash", DateTimeOffset.UtcNow.AddMinutes(1));

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task FindHeuristicRecurrenceCandidatesAsync_DifferentOwner_ExcludesOtherOwnersEntries()
    {
        var opA = await OpenOperationAsync(OwnerA);
        var beginA = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = opA.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            NamespaceId = opA.NamespaceId,
            EntityNameSnapshot = "orders-dlq",
            BodyHash = "shared-hash",
            TargetEntity = "orders-dlq",
        });
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = beginA.Value.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
            MarkerApplied = false,
        });

        var candidatesForOwnerB = await _service.FindHeuristicRecurrenceCandidatesAsync(
            OwnerB, opA.NamespaceId, "orders-dlq", "shared-hash", DateTimeOffset.UtcNow.AddMinutes(1));

        candidatesForOwnerB.Should().BeEmpty();
    }

    // ── RecordDeclinedAsync / FindLineageMatchesAsync (Phase A) ──────────────

    [Fact]
    public async Task RecordDeclinedAsync_WritesTerminalDeclinedEntryWithEligibilityDeclinedEvent()
    {
        var operation = await OpenOperationAsync();

        var result = await _service.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = "orders-dlq",
                BodyHash = "shared-hash",
                TargetEntity = "orders-dlq",
            },
            "RECURRENCE_CAP_EXCEEDED",
            detailJson: null);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;
        entry.State.Should().Be(RecoveryEntryState.Declined);
        entry.Disposition.Should().Be(RecoveryDisposition.Declined);
        entry.ClosedAt.Should().NotBeNull();

        var events = await _service.GetEventsForOperationAsync(operation.Id, OwnerA);
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.EligibilityDeclined
                                            && e.EntryId == entry.Id
                                            && e.DetailJson!.Contains("RECURRENCE_CAP_EXCEEDED"));
    }

    [Fact]
    public async Task RecordDeclinedAsync_ParticipatesInHashChain()
    {
        var operation = await OpenOperationAsync();

        await _service.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                BodyHash = "shared-hash",
                TargetEntity = "orders-dlq",
            },
            "RECURRENCE_CAP_EXCEEDED",
            detailJson: null);

        var chain = await _service.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RecordDeclinedAsync_OperationBelongsToDifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _service.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = OwnerB,
                Actor = Actor(),
                BodyHash = "shared-hash",
                TargetEntity = "orders-dlq",
            },
            "RECURRENCE_CAP_EXCEEDED",
            detailJson: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordDeclinedAsync_DifferentOwner_NotVisibleViaOwnerScopedQueries()
    {
        var opA = await OpenOperationAsync(OwnerA);
        await _service.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = opA.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                BodyHash = "shared-hash",
                TargetEntity = "orders-dlq",
            },
            "RECURRENCE_CAP_EXCEEDED",
            detailJson: null);

        var ownerBEntries = await _service.QueryEntriesAsync(new RecoveryEntryQuery { OwnerId = OwnerB });
        ownerBEntries.Should().BeEmpty();

        var ownerBEvents = await _service.GetEventsForOperationAsync(opA.Id, OwnerB);
        ownerBEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task FindLineageMatchesAsync_ReturnsOwnerAndWindowScopedSet()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;

        // Seeded directly against the DbContext (bypassing BeginEntryAsync, which always stamps
        // BegunAt = UtcNow) so each entry can simulate a specific point in the past for the
        // 90-day window-boundary assertion. Insert is unrestricted by the append-only guard —
        // only Modified/Deleted are — so this is a legitimate way to seed fixture data.
        RecoveryLedgerEntry Seed(DateTimeOffset begunAt, string ownerId = OwnerA, string bodyHash = "shared-hash")
        {
            var entry = new RecoveryLedgerEntry
            {
                OperationId = operation.Id,
                OwnerId = ownerId,
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = "orders-dlq",
                BodyHash = bodyHash,
                TargetEntity = "orders-dlq",
                BegunAt = begunAt,
                State = RecoveryEntryState.Observing,
            };
            _dbContext.RecoveryLedgerEntries.Add(entry);
            return entry;
        }

        var withinWindow = Seed(now.AddDays(-89));
        var outsideWindow = Seed(now.AddDays(-91));
        var otherOwner = Seed(now, ownerId: OwnerB);
        var otherHash = Seed(now, bodyHash: "different-hash");
        await _dbContext.SaveChangesAsync();

        var matches = await _service.FindLineageMatchesAsync(
            OwnerA, operation.NamespaceId, "orders-dlq", "shared-hash", now.AddDays(-90));

        matches.Select(m => m.Id).Should().BeEquivalentTo(new[] { withinWindow.Id });
        matches.Select(m => m.Id).Should().NotContain(new[] { outsideWindow.Id, otherOwner.Id, otherHash.Id });
    }

    [Fact]
    public async Task FindEntriesForEntitySinceAsync_ReturnsOwnerNamespaceEntityAndSinceScopedSetOldestFirst()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;

        RecoveryLedgerEntry Seed(
            DateTimeOffset begunAt, string ownerId = OwnerA, Guid? namespaceId = null, string entityName = "orders-dlq")
        {
            var entry = new RecoveryLedgerEntry
            {
                OperationId = operation.Id,
                OwnerId = ownerId,
                NamespaceId = namespaceId ?? operation.NamespaceId,
                EntityNameSnapshot = entityName,
                BodyHash = "irrelevant-hash",
                TargetEntity = entityName,
                BegunAt = begunAt,
                State = RecoveryEntryState.Observing,
            };
            _dbContext.RecoveryLedgerEntries.Add(entry);
            return entry;
        }

        var earlier = Seed(now.AddMinutes(-5));
        var later = Seed(now.AddMinutes(5));
        var beforeSince = Seed(now.AddMinutes(-10));
        var otherOwner = Seed(now, ownerId: OwnerB);
        var otherEntity = Seed(now, entityName: "payments-dlq");
        var otherNamespace = Seed(now, namespaceId: Guid.NewGuid());
        await _dbContext.SaveChangesAsync();

        var matches = await _service.FindEntriesForEntitySinceAsync(
            OwnerA, operation.NamespaceId, "orders-dlq", now.AddMinutes(-6));

        matches.Select(m => m.Id).Should().Equal(earlier.Id, later.Id);
        matches.Select(m => m.Id).Should().NotContain(new[] { beforeSince.Id, otherOwner.Id, otherEntity.Id, otherNamespace.Id });
    }

    [Fact]
    public async Task FindEntriesForEntitySinceAsync_RespectsLimit()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            _dbContext.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = "orders-dlq",
                BodyHash = "irrelevant-hash",
                TargetEntity = "orders-dlq",
                BegunAt = now.AddMinutes(i),
                State = RecoveryEntryState.Observing,
            });
        }
        await _dbContext.SaveChangesAsync();

        var matches = await _service.FindEntriesForEntitySinceAsync(
            OwnerA, operation.NamespaceId, "orders-dlq", now.AddMinutes(-1), limit: 2);

        matches.Should().HaveCount(2);
    }

    [Fact]
    public async Task FindEntriesForSignatureSinceAsync_ReturnsOwnerSignatureAndSinceScopedSetOldestFirst()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;
        const string signatureHash = "sig-abc123";

        RecoveryLedgerEntry Seed(
            DateTimeOffset begunAt, string ownerId = OwnerA, string? signature = signatureHash, string entityName = "orders-dlq")
        {
            var entry = new RecoveryLedgerEntry
            {
                OperationId = operation.Id,
                OwnerId = ownerId,
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = entityName,
                SignatureHashSnapshot = signature,
                BodyHash = "irrelevant-hash",
                TargetEntity = entityName,
                BegunAt = begunAt,
                State = RecoveryEntryState.Observing,
            };
            _dbContext.RecoveryLedgerEntries.Add(entry);
            return entry;
        }

        var earlier = Seed(now.AddMinutes(-5));
        var later = Seed(now.AddMinutes(5));
        var beforeSince = Seed(now.AddMinutes(-10));
        var otherOwner = Seed(now, ownerId: OwnerB);
        var otherSignature = Seed(now, signature: "sig-different");
        // Different entity, same signature: still expected to match — the signature-scoped join
        // is deliberately not narrowed by entity (see IRecoveryLedger's doc comment).
        var differentEntitySameSignature = Seed(now, entityName: "other-entity");
        var noSignature = Seed(now, signature: null);
        await _dbContext.SaveChangesAsync();

        var matches = await _service.FindEntriesForSignatureSinceAsync(
            OwnerA, signatureHash, now.AddMinutes(-6));

        matches.Select(m => m.Id).Should().Equal(earlier.Id, differentEntitySameSignature.Id, later.Id);
        matches.Select(m => m.Id).Should().NotContain(new[] { beforeSince.Id, otherOwner.Id, otherSignature.Id, noSignature.Id });
    }

    [Fact]
    public async Task FindEntriesForSignatureSinceAsync_RespectsLimit()
    {
        var operation = await OpenOperationAsync();
        var now = DateTimeOffset.UtcNow;
        const string signatureHash = "sig-abc123";

        for (var i = 0; i < 3; i++)
        {
            _dbContext.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                NamespaceId = operation.NamespaceId,
                EntityNameSnapshot = "orders-dlq",
                SignatureHashSnapshot = signatureHash,
                BodyHash = "irrelevant-hash",
                TargetEntity = "orders-dlq",
                BegunAt = now.AddMinutes(i),
                State = RecoveryEntryState.Observing,
            });
        }
        await _dbContext.SaveChangesAsync();

        var matches = await _service.FindEntriesForSignatureSinceAsync(
            OwnerA, signatureHash, now.AddMinutes(-1), limit: 2);

        matches.Should().HaveCount(2);
    }

    // ── Illegal transitions ─────────────────────────────────────────────────

    [Fact]
    public async Task RecordObservationAsync_BeforeExecuted_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task RecordExecutionAsync_AlreadyTerminal_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var second = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        second.IsFailure.Should().BeTrue();
        second.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SetDispositionAsync_AlreadyTerminal_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var result = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "too late");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SetDispositionAsync_EmptyReason_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task RecordObservationAsync_RecurrenceObservedWithoutConfidence_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = null,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    // ── Owner isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task BeginEntryAsync_OperationBelongsToDifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerB,
            Actor = Actor(),
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordExecutionAsync_EntryBelongsToDifferentOwner_ReturnsNotFound()
    {
        var (_, entry) = await OpenAndBeginAsync(OwnerA);

        var result = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerB,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetOperationAsync_WrongOwner_ReturnsNull()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _service.GetOperationAsync(operation.Id, OwnerB);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryEntriesAsync_OnlyReturnsRequestedOwnersEntries()
    {
        var (_, entryA) = await OpenAndBeginAsync(OwnerA);
        var (_, entryB) = await OpenAndBeginAsync(OwnerB);

        var resultA = await _service.QueryEntriesAsync(new RecoveryEntryQuery { OwnerId = OwnerA });

        resultA.Should().ContainSingle(e => e.Id == entryA.Id);
        resultA.Should().NotContain(e => e.Id == entryB.Id);
    }

    [Fact]
    public async Task QueryEntriesAsync_DlqMessageIdFilter_ReturnsOnlyThatMessagesEntries()
    {
        var operation = await OpenOperationAsync();
        var matching = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            DlqMessageId = 42,
            BodyHash = "hash-42",
            TargetEntity = "orders-dlq",
        });
        await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            DlqMessageId = 43,
            BodyHash = "hash-43",
            TargetEntity = "orders-dlq",
        });

        var result = await _service.QueryEntriesAsync(new RecoveryEntryQuery { OwnerId = OwnerA, DlqMessageId = 42 });

        result.Should().ContainSingle(e => e.Id == matching.Value.Id);
    }

    // ── Bookkeeping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LastEventSeq_AdvancesWithEachAppendedEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();
        var seqAfterBegin = entry.LastEventSeq;
        seqAfterBegin.Should().BeGreaterThan(0);

        var afterExecution = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        afterExecution.Value.LastEventSeq.Should().BeGreaterThan(seqAfterBegin);
    }

    // ── Concurrency: two owners' Seq sequences advance independently ────────
    //
    // A single DbContext instance cannot safely run concurrent operations (EF Core throws if a
    // second operation starts before the first completes), so genuine concurrent writers need
    // separate DbContext/connection instances — exactly how ASP.NET Core's per-request Scoped
    // DlqDbContext behaves in production. This test uses a temp-file SQLite database (rather than
    // the class fixture's :memory: connection) so multiple real connections can write to it at
    // once, serialized by SQLite itself plus RecoveryLedgerService's static per-owner semaphore.

    [Fact]
    public async Task ConcurrentAppends_TwoOwners_SequencesAdvanceIndependentlyAndContiguously()
    {
        const int perOwnerCount = 5;
        var dbPath = Path.Combine(Path.GetTempPath(), $"recovery-ledger-concurrency-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await using (var schemaContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options))
            {
                await schemaContext.Database.EnsureCreatedAsync();
            }

            async Task RunForOwnerAsync(string ownerId)
            {
                for (var i = 0; i < perOwnerCount; i++)
                {
                    await using var dbContext = new DlqDbContext(
                        new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);
                    var service = new RecoveryLedgerService(dbContext);

                    var result = await service.OpenOperationAsync(new OpenRecoveryOperationRequest
                    {
                        OwnerId = ownerId,
                        Kind = RecoveryOperationKind.Replay,
                        Trigger = RecoveryTrigger.Manual,
                        Actor = Actor(),
                        ScopeDescription = "entity=orders-dlq",
                        TargetCount = 1,
                    });

                    result.IsSuccess.Should().BeTrue();
                }
            }

            await Task.WhenAll(RunForOwnerAsync(OwnerA), RunForOwnerAsync(OwnerB));

            await using var verifyContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);

            var ownerASeqs = await verifyContext.RecoveryEvents
                .Where(e => e.OwnerId == OwnerA).Select(e => e.Seq).OrderBy(s => s).ToListAsync();
            var ownerBSeqs = await verifyContext.RecoveryEvents
                .Where(e => e.OwnerId == OwnerB).Select(e => e.Seq).OrderBy(s => s).ToListAsync();

            ownerASeqs.Should().Equal(Enumerable.Range(1, perOwnerCount).Select(i => (long)i));
            ownerBSeqs.Should().Equal(Enumerable.Range(1, perOwnerCount).Select(i => (long)i));

            var verifyService = new RecoveryLedgerService(verifyContext);
            (await verifyService.VerifyChainAsync(OwnerA)).IsValid.Should().BeTrue();
            (await verifyService.VerifyChainAsync(OwnerB)).IsValid.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    // ── GetEventsForOperationAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetEventsForOperationAsync_ReturnsSeqOrderedEventsForThatOperationOnly()
    {
        var (operationA, entryA) = await OpenAndBeginAsync();
        var operationB = await OpenOperationAsync();

        var events = await _service.GetEventsForOperationAsync(operationA.Id, OwnerA);

        events.Should().HaveCount(2); // OperationOpened + EntryBegun
        events.Should().BeInAscendingOrder(e => e.Seq);
        events.Should().OnlyContain(e => e.OperationId == operationA.Id);
        events.Should().NotContain(e => e.OperationId == operationB.Id);
    }

    [Fact]
    public async Task GetEventsForOperationAsync_DifferentOwner_ReturnsEmpty()
    {
        var (operation, _) = await OpenAndBeginAsync(OwnerB);

        var events = await _service.GetEventsForOperationAsync(operation.Id, OwnerA);

        events.Should().BeEmpty();
    }

    // ── FlagAgeingAsync / HasAgeingFlagAsync / ExpireEntryAsync ─────────────

    [Fact]
    public async Task FlagAgeingAsync_NonTerminalEntry_AppendsAgeingFlaggedEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(RecoveryEntryState.Executing); // unchanged
        (await _service.HasAgeingFlagAsync(entry.Id, OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task FlagAgeingAsync_CalledTwice_IsIdempotent_NoDuplicateEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();

        await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);
        await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 11);

        var flagEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.AgeingFlagged)
            .ToListAsync();
        flagEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task FlagAgeingAsync_AlreadyTerminalEntry_NoOpsWithoutError()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        var result = await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);

        result.IsSuccess.Should().BeTrue();
        (await _service.HasAgeingFlagAsync(entry.Id, OwnerA)).Should().BeFalse();
    }

    [Fact]
    public async Task FlagAgeingAsync_DifferentOwner_ReturnsNotFound()
    {
        var (_, entry) = await OpenAndBeginAsync(OwnerB);

        var result = await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ExpireEntryAsync_WithoutPriorFlag_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.ExpireEntryAsync(entry.Id, OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        entry = (await _dbContext.RecoveryLedgerEntries.FindAsync(entry.Id))!;
        entry.State.Should().NotBe(RecoveryEntryState.Expired);
    }

    [Fact]
    public async Task ExpireEntryAsync_AfterFlag_TransitionsToExpired()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);

        var result = await _service.ExpireEntryAsync(entry.Id, OwnerA, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(RecoveryEntryState.Expired);
        result.Value.Disposition.Should().Be(RecoveryDisposition.Expired);
        result.Value.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExpireEntryAsync_WhenFlagIsNotTheMostRecentEvent_Fails()
    {
        // Roadmap §7.2: Expired is reachable only through a transition whose *immediately
        // preceding* event is AgeingFlagged — an event appended afterwards (e.g. an operator
        // note) breaks that adjacency and must block expiry.
        var (_, entry) = await OpenAndBeginAsync();
        await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);
        await _service.AppendNoteAsync(entry.Id, OwnerA, Actor(), "operator is investigating");

        var result = await _service.ExpireEntryAsync(entry.Id, OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task ExpireEntryAsync_AlreadyTerminalEntry_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        var result = await _service.ExpireEntryAsync(entry.Id, OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task ExpireEntryAsync_DifferentOwner_ReturnsNotFound()
    {
        var (_, entry) = await OpenAndBeginAsync(OwnerB);

        var result = await _service.ExpireEntryAsync(entry.Id, OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task FlagThenExpire_ConcurrentDuplicateCalls_ProduceOnlyOneExpiryEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.FlagAgeingAsync(entry.Id, OwnerA, Actor(), ageInDays: 10);

        // Two "workers" racing to expire the same already-flagged entry — the ledger's per-owner
        // serialisation means the second call observes the first's completed transition and must
        // fail cleanly rather than double-close it.
        var results = await Task.WhenAll(
            _service.ExpireEntryAsync(entry.Id, OwnerA, Actor()),
            _service.ExpireEntryAsync(entry.Id, OwnerA, Actor()));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Count(r => r.IsFailure).Should().Be(1);

        var dispositionEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.DispositionSet)
            .ToListAsync();
        dispositionEvents.Should().ContainSingle();
    }

    // ── RecordAutonomyGrantTransitionAsync (Phase D Task 1) ───────────────────

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_FirstPromotion_CreatesGrantAndPromotedEvent()
    {
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-1", RecoveryOperationKind.Replay,
            AutonomyLevel.Observe, AutonomyLevel.Approve, "earned via L1 knowledge entry", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.OwnerId.Should().Be(OwnerA);
        result.Value.SignatureHash.Should().Be("sig-1");
        result.Value.ActionKind.Should().Be(RecoveryOperationKind.Replay);
        result.Value.CurrentLevel.Should().Be(AutonomyLevel.Approve);

        var grants = await _dbContext.AutonomyGrants.Where(g => g.SignatureHash == "sig-1").ToListAsync();
        grants.Should().ContainSingle();

        var events = await _dbContext.RecoveryEvents
            .Where(e => e.EventType == RecoveryEventType.AutonomyGrantPromoted)
            .ToListAsync();
        var promoted = events.Should().ContainSingle().Subject;
        promoted.EntryId.Should().BeNull();
        promoted.DetailJson.Should().Contain("sig-1").And.Contain("earned via L1 knowledge entry");

        var operation = await _dbContext.RecoveryOperations.SingleAsync(o => o.Id == promoted.OperationId);
        operation.Kind.Should().Be(RecoveryOperationKind.AutonomyGrantChange);
        operation.Trigger.Should().Be(RecoveryTrigger.AutonomyEvaluation);
        operation.ActorKind.Should().Be(RecoveryActorKind.System);
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_Demotion_UpdatesExistingGrantAndWritesDemotedEvent()
    {
        var first = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-2", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "10 verified successes", null);
        first.IsSuccess.Should().BeTrue();
        var grantId = first.Value.Id;

        var second = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-2", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "verified_success_rate dropped below 95%", null);

        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().Be(grantId, "a demotion updates the existing projection row, never inserts a second one");
        second.Value.CurrentLevel.Should().Be(AutonomyLevel.Approve);

        (await _dbContext.AutonomyGrants.CountAsync(g => g.SignatureHash == "sig-2")).Should().Be(1);

        var demotedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EventType == RecoveryEventType.AutonomyGrantDemoted)
            .ToListAsync();
        demotedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_EmptyReason_Fails()
    {
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-3", RecoveryOperationKind.Replay,
            AutonomyLevel.Observe, AutonomyLevel.Approve, "  ", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-3")).Should().BeFalse();
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_SameLevel_Fails()
    {
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-4", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Approve, "not a real transition", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_DifferentOwners_ProduceIsolatedGrants()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "shared-sig", RecoveryOperationKind.Replay,
            AutonomyLevel.Observe, AutonomyLevel.Approve, "owner A earns it", null);
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerB, "shared-sig", RecoveryOperationKind.Replay,
            AutonomyLevel.Observe, AutonomyLevel.Standing, "owner B earns it independently", null);

        var grantA = await _dbContext.AutonomyGrants.SingleAsync(g => g.OwnerId == OwnerA && g.SignatureHash == "shared-sig");
        var grantB = await _dbContext.AutonomyGrants.SingleAsync(g => g.OwnerId == OwnerB && g.SignatureHash == "shared-sig");

        grantA.CurrentLevel.Should().Be(AutonomyLevel.Approve);
        grantB.CurrentLevel.Should().Be(AutonomyLevel.Standing);
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_ParticipatesInHashChain()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-5", RecoveryOperationKind.Replay,
            AutonomyLevel.Observe, AutonomyLevel.Approve, "reason", null);

        var chain = await _service.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_WithEvidenceJson_EmbedsEvidenceAsNestedJson()
    {
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-6", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "reason",
            evidenceJson: "{\"n\":12,\"verifiedSuccessRate\":0.95}");

        var evt = await _dbContext.RecoveryEvents.SingleAsync(e => e.EventType == RecoveryEventType.AutonomyGrantPromoted);
        evt.DetailJson.Should().Contain("\"verifiedSuccessRate\":0.95");
        // Nested as a real JSON object, not a double-escaped string.
        evt.DetailJson.Should().NotContain("\\\"n\\\"");
        result.IsSuccess.Should().BeTrue();
    }

    // ── GetAutonomyGrantAsync (Phase D, this increment) ────────────────────────

    [Fact]
    public async Task GetAutonomyGrantAsync_NoGrantEverWritten_ReturnsNull()
    {
        (await _service.GetAutonomyGrantAsync(OwnerA, "sig-never-granted", RecoveryOperationKind.Replay))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetAutonomyGrantAsync_AfterPromotion_ReturnsCurrentProjection()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-7", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "10 verified successes", null);

        var grant = await _service.GetAutonomyGrantAsync(OwnerA, "sig-7", RecoveryOperationKind.Replay);

        grant.Should().NotBeNull();
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Standing);
    }

    [Fact]
    public async Task GetAutonomyGrantAsync_DifferentOwnerSameSignature_ReturnsNull()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-8", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner A earned it", null);

        (await _service.GetAutonomyGrantAsync(OwnerB, "sig-8", RecoveryOperationKind.Replay))
            .Should().BeNull("a grant is scoped to its owner — owner isolation must hold on the read side too");
    }

    // ── GetAutonomyGrantsAsync / GetRecentAutonomyTransitionsAsync (fleet-wide autonomy dashboard, roadmap §11 item 5) ──

    [Fact]
    public async Task GetAutonomyGrantsAsync_NoGrantsForOwner_ReturnsEmpty()
    {
        (await _service.GetAutonomyGrantsAsync(OwnerA)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAutonomyGrantsAsync_ReturnsOnlyCallerOwnedGrants()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-1", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner A's grant", null);
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerB, "sig-dash-2", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner B's grant", null);

        var grants = await _service.GetAutonomyGrantsAsync(OwnerA);

        grants.Should().ContainSingle().Which.SignatureHash.Should().Be("sig-dash-1");
    }

    [Fact]
    public async Task GetAutonomyGrantsAsync_AfterDemotion_ReflectsCurrentProjectionNotHistory()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-3", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "promoted", null);
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-3", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "demoted back", null);

        var grants = await _service.GetAutonomyGrantsAsync(OwnerA);

        grants.Should().ContainSingle().Which.CurrentLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public async Task GetRecentAutonomyTransitionsAsync_NoTransitions_ReturnsEmpty()
    {
        (await _service.GetRecentAutonomyTransitionsAsync(OwnerA, 20)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentAutonomyTransitionsAsync_ReturnsNewestFirstDecodedFromDetailJson()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-4", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "first promotion", null);
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-4", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Unattended, "second promotion", null);

        var transitions = await _service.GetRecentAutonomyTransitionsAsync(OwnerA, 20);

        transitions.Should().HaveCount(2);
        transitions[0].Reason.Should().Be("second promotion");
        transitions[0].PreviousLevel.Should().Be(AutonomyLevel.Standing);
        transitions[0].NewLevel.Should().Be(AutonomyLevel.Unattended);
        transitions[1].Reason.Should().Be("first promotion");
    }

    [Fact]
    public async Task GetRecentAutonomyTransitionsAsync_DifferentOwner_ExcludesOtherOwnersTransitions()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-dash-5", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner A's transition", null);
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerB, "sig-dash-6", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner B's transition", null);

        var transitions = await _service.GetRecentAutonomyTransitionsAsync(OwnerA, 20);

        transitions.Should().ContainSingle().Which.SignatureHash.Should().Be("sig-dash-5");
    }

    [Fact]
    public async Task GetRecentAutonomyTransitionsAsync_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.RecordAutonomyGrantTransitionAsync(
                OwnerA, $"sig-dash-limit-{i}", RecoveryOperationKind.Replay,
                AutonomyLevel.Approve, AutonomyLevel.Standing, $"promotion {i}", null);
        }

        var transitions = await _service.GetRecentAutonomyTransitionsAsync(OwnerA, 3);

        transitions.Should().HaveCount(3);
    }

    // ── Emergency Stop: IsEmergencyStopActiveAsync / RecordEmergencyControlEventAsync (§9.4.2, §15.2) ──

    [Fact]
    public async Task IsEmergencyStopActiveAsync_NoEventsForOwner_ReturnsFalse()
    {
        (await _service.IsEmergencyStopActiveAsync(OwnerA)).Should().BeFalse();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_Activate_WritesEmergencyControlOperationAndActivatedEvent()
    {
        var result = await _service.RecordEmergencyControlEventAsync(
            OwnerA, Actor("admin-1"), activate: true, reason: "suspected duplicate replays");

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(RecoveryOperationKind.EmergencyControl);
        result.Value.Trigger.Should().Be(RecoveryTrigger.EmergencyControl);
        result.Value.NamespaceId.Should().BeNull();
        result.Value.TargetCount.Should().Be(0);

        var evt = await _dbContext.RecoveryEvents
            .SingleAsync(e => e.EventType == RecoveryEventType.EmergencyStopActivated);
        evt.EntryId.Should().BeNull();
        evt.OperationId.Should().Be(result.Value.Id);
        evt.DetailJson.Should().Contain("suspected duplicate replays");
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_Activate_IsEmergencyStopActiveAsyncReturnsTrue()
    {
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: null);

        (await _service.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_ActivateThenClear_IsEmergencyStopActiveAsyncReturnsFalse()
    {
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: null);
        var clearResult = await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: false, reason: "incident resolved");

        clearResult.IsSuccess.Should().BeTrue();
        (await _service.IsEmergencyStopActiveAsync(OwnerA)).Should().BeFalse();

        var clearedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EventType == RecoveryEventType.EmergencyStopCleared)
            .ToListAsync();
        clearedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_RepeatedActivation_AlwaysAppendsFreshEvent()
    {
        // Roadmap §9.4.2: re-activating during an unresolved incident is itself a meaningful,
        // separately timestamped fact — never suppressed as a no-op.
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: "first");
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: "second");

        var activatedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EventType == RecoveryEventType.EmergencyStopActivated)
            .ToListAsync();
        activatedEvents.Should().HaveCount(2);
        (await _service.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_DifferentOwners_AreIsolated()
    {
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: null);

        (await _service.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
        (await _service.IsEmergencyStopActiveAsync(OwnerB)).Should().BeFalse();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_ParticipatesInHashChain()
    {
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: null);
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: false, reason: null);

        var chain = await _service.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_DoesNotTouchExistingAutonomyGrants()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-untouched", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "earned via history", null);

        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: true, reason: null);
        await _service.RecordEmergencyControlEventAsync(OwnerA, Actor(), activate: false, reason: null);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-untouched");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Standing, "emergency stop must never modify an existing AutonomyGrant");

        (await _dbContext.RecoveryEvents.CountAsync(e =>
            e.EventType == RecoveryEventType.AutonomyGrantPromoted || e.EventType == RecoveryEventType.AutonomyGrantDemoted))
            .Should().Be(1, "only the seeded transition, not the emergency-stop calls, may produce a grant event");
    }

    [Fact]
    public async Task RecordEmergencyControlEventAsync_ConcurrentCallsSameOwner_ProduceContiguousValidChain()
    {
        // Same real-connection/temp-file pattern as ConcurrentAppends_TwoOwners_..., proving the
        // per-owner semaphore (RecoveryLedgerService.AcquireOwnerLockAsync) serializes concurrent
        // emergency-stop activate/clear calls exactly as it does every other ledger write — no
        // corrupted hash chain, no lost event, regardless of call interleaving.
        const int callCount = 6;
        var dbPath = Path.Combine(Path.GetTempPath(), $"emergency-stop-concurrency-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await using (var schemaContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options))
            {
                await schemaContext.Database.EnsureCreatedAsync();
            }

            async Task ToggleAsync(bool activate)
            {
                await using var dbContext = new DlqDbContext(
                    new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);
                var service = new RecoveryLedgerService(dbContext);

                var result = await service.RecordEmergencyControlEventAsync(
                    OwnerA, Actor(), activate, reason: null);

                result.IsSuccess.Should().BeTrue();
            }

            await Task.WhenAll(Enumerable.Range(0, callCount).Select(i => ToggleAsync(i % 2 == 0)));

            await using var verifyContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);

            var seqs = await verifyContext.RecoveryEvents
                .Where(e => e.OwnerId == OwnerA).Select(e => e.Seq).OrderBy(s => s).ToListAsync();
            seqs.Should().Equal(Enumerable.Range(1, callCount).Select(i => (long)i));

            var verifyService = new RecoveryLedgerService(verifyContext);
            (await verifyService.VerifyChainAsync(OwnerA)).IsValid.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    // ── RecordRecurrenceContextAsync (roadmap §9.4.1) ─────────────────────────

    [Fact]
    public async Task RecordRecurrenceContextAsync_WritesRecurrenceCapObservedEventOnTheRealEntry()
    {
        var operation = await OpenOperationAsync();
        var entry = await BeginEntryAsync(operation);

        var result = await _service.RecordRecurrenceContextAsync(
            entry.Id, OwnerA, Actor(), "RECURRENCE_CAP_EXCEEDED", matchedCount: 3);

        result.IsSuccess.Should().BeTrue();
        result.Value.EventType.Should().Be(RecoveryEventType.RecurrenceCapObserved);
        result.Value.EntryId.Should().Be(entry.Id);
        result.Value.DetailJson.Should().Contain("RECURRENCE_CAP_EXCEEDED").And.Contain("3");

        // Never a fabricated Declined entry (§29.11 rule 2) — the real entry's own state is
        // untouched by recording this context.
        var reloaded = await _dbContext.RecoveryLedgerEntries.FindAsync(entry.Id);
        reloaded!.State.Should().Be(entry.State);
        reloaded.Disposition.Should().BeNull();
    }

    [Fact]
    public async Task RecordRecurrenceContextAsync_ParticipatesInHashChain()
    {
        var operation = await OpenOperationAsync();
        var entry = await BeginEntryAsync(operation);

        await _service.RecordRecurrenceContextAsync(
            entry.Id, OwnerA, Actor(), "RECURRENCE_CAP_EXCEEDED_HEURISTIC", matchedCount: 5);

        var chain = await _service.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RecordRecurrenceContextAsync_UnknownEntry_ReturnsNotFound()
    {
        var result = await _service.RecordRecurrenceContextAsync(
            Guid.NewGuid(), OwnerA, Actor(), "RECURRENCE_CAP_EXCEEDED", matchedCount: 3);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordRecurrenceContextAsync_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        var result = await _service.RecordRecurrenceContextAsync(
            entry.Id, OwnerB, Actor(), "RECURRENCE_CAP_EXCEEDED", matchedCount: 3);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ── RecordOutcomeFlagAsync / HasUnsafeOutcomeFlagAsync / HasDuplicateAssociationAsync (roadmap §8.10, §9.3) ──

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(RecoveryOperation operation, string? signatureHash, string bodyHash)
    {
        var result = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = operation.OwnerId,
            Actor = Actor(),
            BodyHash = bodyHash,
            SignatureHashSnapshot = signatureHash,
            TargetEntity = "orders-dlq",
        });

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_WritesOutcomeFlaggedEventOnTheEntry()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        var result = await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "customer reported data loss");

        result.IsSuccess.Should().BeTrue();
        result.Value.EventType.Should().Be(RecoveryEventType.OutcomeFlagged);
        result.Value.EntryId.Should().Be(entry.Id);
        result.Value.DetailJson.Should().Contain("Unsafe").And.Contain("customer reported data loss");

        // Never a state transition — the entry's own state is untouched by recording this flag.
        var reloaded = await _dbContext.RecoveryLedgerEntries.FindAsync(entry.Id);
        reloaded!.State.Should().Be(entry.State);
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_ParticipatesInHashChain()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "double-charged customer");

        var chain = await _service.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_EmptyReason_ReturnsValidationError()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        var result = await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_UnknownEntry_ReturnsNotFound()
    {
        var result = await _service.RecordOutcomeFlagAsync(
            Guid.NewGuid(), OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "reason");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        var result = await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerB, Actor(), RecoveryOutcomeFlagKind.Unsafe, "reason");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordOutcomeFlagAsync_LegalAgainstATerminalEntry()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);
        var writeOff = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "unrecoverable");
        writeOff.IsSuccess.Should().BeTrue();

        var result = await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "flagged after write-off");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HasUnsafeOutcomeFlagAsync_NoFlags_ReturnsFalse()
    {
        var operation = await OpenOperationAsync(OwnerA);
        await BeginEntryAsync(operation);

        var result = await _service.HasUnsafeOutcomeFlagAsync(OwnerA);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasUnsafeOutcomeFlagAsync_UnsafeFlagOnAnyEntry_ReturnsTrue_FleetLevel()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var flaggedEntry = await BeginEntryAsync(operation, "sig-x", "body-flagged");
        await BeginEntryAsync(operation, "sig-y", "body-other");

        await _service.RecordOutcomeFlagAsync(
            flaggedEntry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "incident");

        var result = await _service.HasUnsafeOutcomeFlagAsync(OwnerA);

        result.Should().BeTrue("unsafe_outcome_count's disqualifying effect is fleet-level, not per-signature (§8.10)");
    }

    [Fact]
    public async Task HasUnsafeOutcomeFlagAsync_DuplicateFlagOnly_ReturnsFalse()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation);

        await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "duplicate");

        var result = await _service.HasUnsafeOutcomeFlagAsync(OwnerA);

        result.Should().BeFalse("a DuplicateBusinessEffect flag must never be mistaken for an Unsafe flag");
    }

    [Fact]
    public async Task HasUnsafeOutcomeFlagAsync_FlagUnderDifferentOwner_ReturnsFalse()
    {
        var operationA = await OpenOperationAsync(OwnerA);
        var entryA = await BeginEntryAsync(operationA);
        await _service.RecordOutcomeFlagAsync(entryA.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "incident");

        var result = await _service.HasUnsafeOutcomeFlagAsync(OwnerB);

        result.Should().BeFalse("owner isolation must hold for the fleet-level unsafe-outcome read");
    }

    [Fact]
    public async Task HasDuplicateAssociationAsync_NoFlags_ReturnsFalse()
    {
        var operation = await OpenOperationAsync(OwnerA);
        await BeginEntryAsync(operation, "sig-x", "body-1");

        var result = await _service.HasDuplicateAssociationAsync(OwnerA, "sig-x");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasDuplicateAssociationAsync_FlagOnThisSignature_ReturnsTrue()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await BeginEntryAsync(operation, "sig-x", "body-1");

        await _service.RecordOutcomeFlagAsync(
            entry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "duplicate");

        var result = await _service.HasDuplicateAssociationAsync(OwnerA, "sig-x");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasDuplicateAssociationAsync_FlagOnADifferentSignature_ReturnsFalse_PerSignature()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var flaggedEntry = await BeginEntryAsync(operation, "sig-x", "body-flagged");
        await BeginEntryAsync(operation, "sig-y", "body-other");

        await _service.RecordOutcomeFlagAsync(
            flaggedEntry.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "duplicate");

        var result = await _service.HasDuplicateAssociationAsync(OwnerA, "sig-y");

        result.Should().BeFalse("duplicate_association never bleeds into an unrelated signature (§8.10)");
    }

    [Fact]
    public async Task HasDuplicateAssociationAsync_FlagUnderDifferentOwner_ReturnsFalse()
    {
        var operationA = await OpenOperationAsync(OwnerA);
        var entryA = await BeginEntryAsync(operationA, "sig-x", "body-1");
        await _service.RecordOutcomeFlagAsync(
            entryA.Id, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "duplicate");

        var result = await _service.HasDuplicateAssociationAsync(OwnerB, "sig-x");

        result.Should().BeFalse("owner isolation must hold even when the signature hash matches");
    }

    // ── GetRecentVerifiedDispositionsAsync (Phase D fast-demotion source query, roadmap §7.6/§8.5/§8.6) ──

    private async Task<RecoveryLedgerEntry> CloseAsRecoveredAsync(RecoveryLedgerEntry entry, string ownerId = OwnerA)
    {
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(), Outcome = RecoveryExecutionOutcome.Accepted,
        });
        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> CloseAsReturnedAsync(RecoveryLedgerEntry entry, string ownerId = OwnerA)
    {
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(), Outcome = RecoveryExecutionOutcome.Accepted,
        });
        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved, Confidence = VerificationConfidence.Exact,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> CloseAsUnverifiedAsync(RecoveryLedgerEntry entry, string ownerId = OwnerA)
    {
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(), Outcome = RecoveryExecutionOutcome.Accepted,
        });
        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(),
            Outcome = RecoveryObservationOutcome.ObservationUnavailable,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> CloseAsFailedAsync(RecoveryLedgerEntry entry, string ownerId = OwnerA)
    {
        var result = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id, OwnerId = ownerId, Actor = Actor(), Outcome = RecoveryExecutionOutcome.Rejected,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_NoEntries_ReturnsEmpty()
    {
        var result = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-none", RecoveryOperationKind.Replay, 2);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_OrdersByLastEventSeq_NotByBegunAt()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry1 = await BeginEntryAsync(operation, "sig-order", "body-1"); // begins 1st
        var entry2 = await BeginEntryAsync(operation, "sig-order", "body-2"); // begins 2nd
        var entry3 = await BeginEntryAsync(operation, "sig-order", "body-3"); // begins 3rd

        await CloseAsRecoveredAsync(entry2); // closes 1st
        await CloseAsReturnedAsync(entry3);  // closes 2nd
        await CloseAsReturnedAsync(entry1);  // closes 3rd (last) — begun first, closed last

        var recent = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-order", RecoveryOperationKind.Replay, 2);

        // Closure order (most-recent-first) is entry1, entry3 — both Returned. Ordering by
        // BegunAt descending would instead yield entry3, entry2 — Returned, Recovered — a
        // different answer, so this assertion only holds if the query truly orders by the
        // authoritative closure sequence (LastEventSeq), not by begin time.
        recent.Should().Equal(RecoveryDisposition.Returned, RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_UnverifiedEntry_ExcludedEntirely_NotStreakBreaking()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry1 = await BeginEntryAsync(operation, "sig-unverified", "body-1");
        var entry2 = await BeginEntryAsync(operation, "sig-unverified", "body-2");
        var entry3 = await BeginEntryAsync(operation, "sig-unverified", "body-3");

        await CloseAsReturnedAsync(entry1);
        await CloseAsUnverifiedAsync(entry2);
        await CloseAsReturnedAsync(entry3);

        var recent = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-unverified", RecoveryOperationKind.Replay, 2);

        // A provider's inability to prove absence is not evidence against the signature (§8.10,
        // §14) — Unverified must not appear in the population at all, so the two most recent
        // *verified* outcomes are the two Returned entries either side of it.
        recent.Should().Equal(RecoveryDisposition.Returned, RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_FailedEntry_NeverAppears()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry1 = await BeginEntryAsync(operation, "sig-failed", "body-1");
        var entry2 = await BeginEntryAsync(operation, "sig-failed", "body-2");

        await CloseAsFailedAsync(entry1);
        await CloseAsReturnedAsync(entry2);

        var recent = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-failed", RecoveryOperationKind.Replay, 2);

        // ExecutionFailed is only ever set by RecordExecutionAsync's rejection branch, before any
        // observation window opens — never by RecordObservationAsync — so it can never be part of
        // "two consecutive verified Returned outcomes" (roadmap: demotion is "driven solely by
        // verified RecordObservationAsync outcomes — two consecutive Returned").
        recent.Should().Equal(RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_RecoveredBreaksTheStreak()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry1 = await BeginEntryAsync(operation, "sig-reset", "body-1");
        var entry2 = await BeginEntryAsync(operation, "sig-reset", "body-2");
        var entry3 = await BeginEntryAsync(operation, "sig-reset", "body-3");

        await CloseAsReturnedAsync(entry1);
        await CloseAsRecoveredAsync(entry2);
        await CloseAsReturnedAsync(entry3);

        var recent = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-reset", RecoveryOperationKind.Replay, 2);

        recent.Should().Equal(RecoveryDisposition.Returned, RecoveryDisposition.Recovered);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_DifferentSignature_Isolated()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entryX = await BeginEntryAsync(operation, "sig-x", "body-x");
        var entryY = await BeginEntryAsync(operation, "sig-y", "body-y");

        await CloseAsReturnedAsync(entryX);
        await CloseAsReturnedAsync(entryY);

        var recent = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-x", RecoveryOperationKind.Replay, 2);

        recent.Should().Equal(RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_DifferentOwner_Isolated()
    {
        var operationA = await OpenOperationAsync(OwnerA);
        var entryA = await BeginEntryAsync(operationA, "sig-shared", "body-a");
        await CloseAsReturnedAsync(entryA, OwnerA);

        var operationB = await OpenOperationAsync(OwnerB);
        var entryB = await BeginEntryAsync(operationB, "sig-shared", "body-b");
        await CloseAsReturnedAsync(entryB, OwnerB);

        var recentA = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-shared", RecoveryOperationKind.Replay, 2);

        recentA.Should().Equal(RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsAsync_DifferentActionKind_Isolated()
    {
        var replayOperation = await OpenOperationAsync(OwnerA, RecoveryOperationKind.Replay);
        var replayEntry = await BeginEntryAsync(replayOperation, "sig-kind", "body-replay");
        await CloseAsReturnedAsync(replayEntry);

        var purgeOperation = await OpenOperationAsync(OwnerA, RecoveryOperationKind.Purge, "test purge");
        var purgeEntry = await BeginEntryAsync(purgeOperation, "sig-kind", "body-purge");
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = purgeEntry.Id, OwnerId = OwnerA, Actor = Actor(), Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var recentReplay = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-kind", RecoveryOperationKind.Replay, 2);
        var recentPurge = await _service.GetRecentVerifiedDispositionsAsync(OwnerA, "sig-kind", RecoveryOperationKind.Purge, 2);

        recentReplay.Should().Equal(RecoveryDisposition.Returned);
        recentPurge.Should().BeEmpty("a Discarded purge disposition is neither Recovered nor Returned");
    }

    // ── GetRecentVerifiedDispositionsByRuleAsync / RecordAutoReplayCircuitBreakerTripAsync (circuit breaker) ──

    [Fact]
    public async Task GetRecentVerifiedDispositionsByRuleAsync_NoEntries_ReturnsEmpty()
    {
        var result = await _service.GetRecentVerifiedDispositionsByRuleAsync(OwnerA, 999, RecoveryOperationKind.Replay, 2);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsByRuleAsync_OrdersByLastEventSeq_NotByBegunAt()
    {
        var operation = await OpenOperationAsync(OwnerA, sourceRuleId: 1);
        var entry1 = await BeginEntryAsync(operation, "sig-a", "body-1"); // begins 1st
        var entry2 = await BeginEntryAsync(operation, "sig-b", "body-2"); // begins 2nd
        var entry3 = await BeginEntryAsync(operation, "sig-c", "body-3"); // begins 3rd

        await CloseAsRecoveredAsync(entry2); // closes 1st
        await CloseAsReturnedAsync(entry3);  // closes 2nd
        await CloseAsReturnedAsync(entry1);  // closes 3rd (last)

        var recent = await _service.GetRecentVerifiedDispositionsByRuleAsync(OwnerA, 1, RecoveryOperationKind.Replay, 2);

        recent.Should().Equal(RecoveryDisposition.Returned, RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsByRuleAsync_UnverifiedEntry_ExcludedEntirely()
    {
        var operation = await OpenOperationAsync(OwnerA, sourceRuleId: 1);
        var entry1 = await BeginEntryAsync(operation, "sig-a", "body-1");
        var entry2 = await BeginEntryAsync(operation, "sig-b", "body-2");
        var entry3 = await BeginEntryAsync(operation, "sig-c", "body-3");

        await CloseAsReturnedAsync(entry1);
        await CloseAsUnverifiedAsync(entry2);
        await CloseAsReturnedAsync(entry3);

        var recent = await _service.GetRecentVerifiedDispositionsByRuleAsync(OwnerA, 1, RecoveryOperationKind.Replay, 2);

        recent.Should().Equal(RecoveryDisposition.Returned, RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsByRuleAsync_DifferentRule_Isolated()
    {
        var operationX = await OpenOperationAsync(OwnerA, sourceRuleId: 1);
        var entryX = await BeginEntryAsync(operationX, "sig-x", "body-x");
        await CloseAsReturnedAsync(entryX);

        var operationY = await OpenOperationAsync(OwnerA, sourceRuleId: 2);
        var entryY = await BeginEntryAsync(operationY, "sig-y", "body-y");
        await CloseAsReturnedAsync(entryY);

        var recentRuleOne = await _service.GetRecentVerifiedDispositionsByRuleAsync(OwnerA, 1, RecoveryOperationKind.Replay, 2);

        recentRuleOne.Should().Equal(RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task GetRecentVerifiedDispositionsByRuleAsync_MultipleSignaturesUnderSameRule_AllCounted()
    {
        // A rule fires against many different failure signatures — the circuit breaker's whole
        // reason for existing is a per-rule, not per-signature, view of verified outcomes.
        var operation = await OpenOperationAsync(OwnerA, sourceRuleId: 1);
        var entry1 = await BeginEntryAsync(operation, "sig-a", "body-1");
        var entry2 = await BeginEntryAsync(operation, "sig-b", "body-2");

        await CloseAsReturnedAsync(entry1);
        await CloseAsRecoveredAsync(entry2);

        var recent = await _service.GetRecentVerifiedDispositionsByRuleAsync(OwnerA, 1, RecoveryOperationKind.Replay, 2);

        recent.Should().Equal(RecoveryDisposition.Recovered, RecoveryDisposition.Returned);
    }

    [Fact]
    public async Task RecordAutoReplayCircuitBreakerTripAsync_WritesAutoReplayRuleControlOperationAndEvent()
    {
        var result = await _service.RecordAutoReplayCircuitBreakerTripAsync(
            OwnerA, ruleId: 42, ruleName: "Poison Message Rule", Actor("system"), sampleSize: 20, verifiedSuccessRate: 0.30);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(RecoveryOperationKind.AutoReplayRuleControl);
        result.Value.Trigger.Should().Be(RecoveryTrigger.AutoReplayCircuitBreaker);
        result.Value.SourceRuleId.Should().Be(42);
        result.Value.NamespaceId.Should().BeNull();

        var evt = await _dbContext.RecoveryEvents
            .SingleAsync(e => e.EventType == RecoveryEventType.AutoReplayRuleCircuitBreakerTripped);
        evt.EntryId.Should().BeNull();
        evt.OperationId.Should().Be(result.Value.Id);
        evt.DetailJson.Should().Contain("Poison Message Rule");
    }

    // ── GetAgeingAsync / GetDistinctSignatureHashesAsync: per-sweep batch limit ─────────────────

    [Fact]
    public async Task GetAgeingAsync_NoLimit_ReturnsAllNonTerminalEntries()
    {
        for (var i = 0; i < 3; i++)
        {
            await OpenAndBeginAsync(OwnerA);
        }

        var result = await _service.GetAgeingAsync(OwnerA);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAgeingAsync_LimitRespected()
    {
        // BegunAt is stamped internally at insert and, correctly, cannot be altered afterward —
        // RecoveryLedgerAppendOnlyGuard rejects it (see RecoveryLedgerAppendOnlyGuardTests) — so
        // this asserts the cap itself, not which specific entries win the tie among rows begun
        // within the same test's real-clock granularity.
        for (var i = 0; i < 3; i++)
        {
            await OpenAndBeginAsync(OwnerA);
        }

        var result = await _service.GetAgeingAsync(OwnerA, limit: 2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDistinctSignatureHashesAsync_NoLimit_ReturnsAllDistinctSignatures()
    {
        var operation = await OpenOperationAsync(OwnerA);
        await BeginEntryAsync(operation, "sig-a", "body-a");
        await BeginEntryAsync(operation, "sig-b", "body-b");
        await BeginEntryAsync(operation, "sig-c", "body-c");

        var result = await _service.GetDistinctSignatureHashesAsync(OwnerA, RecoveryOperationKind.Replay);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetDistinctSignatureHashesAsync_LimitRespected()
    {
        var operation = await OpenOperationAsync(OwnerA);
        await BeginEntryAsync(operation, "sig-a", "body-a");
        await BeginEntryAsync(operation, "sig-b", "body-b");
        await BeginEntryAsync(operation, "sig-c", "body-c");

        var result = await _service.GetDistinctSignatureHashesAsync(OwnerA, RecoveryOperationKind.Replay, limit: 2);

        result.Should().HaveCount(2);
        // The query orders by the hash string for determinism when capped.
        result.Should().Equal("sig-a", "sig-b");
    }

    // ── RecordAutonomyGrantTransitionAsync stale-previousLevel guard (Phase D fast-demotion increment) ──

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_StalePreviousLevel_ReturnsConflict_NoOrphanedEvent()
    {
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-race", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "promoted for test", null);

        // The winning writer (e.g. event-time fast demotion) already demoted the grant.
        await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-race", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "winner: two consecutive Returned", null);

        // A second, independent writer (e.g. the hourly sweep) still holding a stale snapshot
        // that believes the grant is Standing attempts the very same transition.
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-race", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "loser: stale snapshot", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);

        var demotedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.OwnerId == OwnerA && e.EventType == RecoveryEventType.AutonomyGrantDemoted)
            .ToListAsync();
        demotedEvents.Should().ContainSingle(
            "the losing writer's stale-snapshot transition must not log a duplicate forensic event");

        var grant = await _service.GetAutonomyGrantAsync(OwnerA, "sig-race", RecoveryOperationKind.Replay);
        grant!.CurrentLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public async Task RecordAutonomyGrantTransitionAsync_FirstCreation_UnaffectedByStaleGuard()
    {
        // First-creation (no existing grant row) has nothing to validate previousLevel against —
        // only an update against an already-existing row is checked.
        var result = await _service.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-fresh", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "first promotion", null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetSignatureProviderAsync_NoLedgerEntry_FallsBackToNamespaceSignatureProvider()
    {
        // A signature that has never been replayed/purged has no RecoveryLedgerEntries row at
        // all, so ProviderSnapshot can't be read from the ledger — this is the everyday case for
        // a brand-new signature. Without the fallback, callers (RecoveryController's
        // GetAutonomyStatus, AutonomyEvaluationWorker) treat the unresolved provider as AWS's
        // stricter capabilities, wrongly telling an operator that a never-yet-replayed Azure
        // signature "cannot currently provide the deterministic recovery evidence" it actually can.
        var namespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(new NamespaceSignature
        {
            NamespaceId = namespaceId,
            OwnerId = OwnerA,
            SignatureHash = "sig-never-replayed",
            FirstSeenAt = DateTimeOffset.UtcNow.AddHours(-1),
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 1,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = "[]",
        });
        await _dbContext.SaveChangesAsync();

        var azureNamespace = Namespace.Create(
            "azure-ns", "PROTECTED:encrypted-data", provider: CloudProviderType.Azure).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(azureNamespace, namespaceId);

        var namespaceRepository = new Mock<INamespaceRepository>();
        namespaceRepository.Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(azureNamespace));

        var service = new RecoveryLedgerService(_dbContext, configuration: null, namespaceRepository.Object);

        var provider = await service.GetSignatureProviderAsync(OwnerA, "sig-never-replayed");

        provider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task GetSignatureProviderAsync_LedgerEntryExists_PrefersLedgerOverNamespaceFallback()
    {
        // Once a real recovery has happened, the ledger's own ProviderSnapshot is the ground
        // truth and must win — proven here by never wiring a namespace repository into _service
        // at all, so a wrong answer could only come from the (untouched) fallback path.
        var operation = await OpenOperationAsync();
        const string signatureHash = "sig-already-replayed";
        _dbContext.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            NamespaceId = operation.NamespaceId,
            EntityNameSnapshot = "orders-dlq",
            SignatureHashSnapshot = signatureHash,
            ProviderSnapshot = CloudProviderType.Gcp,
            BodyHash = "irrelevant-hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
            State = RecoveryEntryState.Observing,
        });
        await _dbContext.SaveChangesAsync();

        var provider = await _service.GetSignatureProviderAsync(OwnerA, signatureHash);

        provider.Should().Be(CloudProviderType.Gcp);
    }
}
