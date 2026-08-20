using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryChainVerifierTests
{
    private const string OwnerId = "owner-a";

    private static RecoveryEvent BuildEvent(long seq, string prevHash, Guid operationId, string? detailJson = null)
    {
        var id = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        const RecoveryEventType eventType = RecoveryEventType.OperatorNote;
        const string actorIdentity = "test-actor";
        const RecoveryActorKind actorKind = RecoveryActorKind.User;
        const int schemaVersion = 1;

        var hash = RecoveryHashChain.ComputeEntryHash(
            id, OwnerId, seq, entryId: null, operationId, eventType, occurredAt,
            actorIdentity, actorKind, detailJson, schemaVersion, prevHash);

        return new RecoveryEvent
        {
            Id = id,
            OwnerId = OwnerId,
            Seq = seq,
            EntryId = null,
            OperationId = operationId,
            EventType = eventType,
            OccurredAt = occurredAt,
            ActorIdentity = actorIdentity,
            ActorKind = actorKind,
            DetailJson = detailJson,
            PrevHash = prevHash,
            EntryHash = hash,
            SchemaVersion = schemaVersion,
        };
    }

    private static RecoveryEvent WithDetailJson(RecoveryEvent source, string detailJson) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        OperationId = source.OperationId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = detailJson,
        PrevHash = source.PrevHash,
        EntryHash = source.EntryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static RecoveryEvent WithEntryHash(RecoveryEvent source, string entryHash) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        OperationId = source.OperationId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = source.DetailJson,
        PrevHash = source.PrevHash,
        EntryHash = entryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static RecoveryEvent WithPrevHash(RecoveryEvent source, string prevHash) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        OperationId = source.OperationId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = source.DetailJson,
        PrevHash = prevHash,
        EntryHash = source.EntryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static List<RecoveryEvent> BuildValidChain(int count)
    {
        var operationId = Guid.NewGuid();
        var events = new List<RecoveryEvent>();
        var prevHash = RecoveryHashChain.GenesisHash;

        for (var seq = 1; seq <= count; seq++)
        {
            var evt = BuildEvent(seq, prevHash, operationId, $"detail-{seq}");
            events.Add(evt);
            prevHash = evt.EntryHash;
        }

        return events;
    }

    [Fact]
    public void Verify_EmptyChain_IsValid()
    {
        var result = RecoveryChainVerifier.Verify(OwnerId, []);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(0);
        result.FirstDivergentSeq.Should().BeNull();
    }

    [Fact]
    public void Verify_ValidChain_IsValid()
    {
        var events = BuildValidChain(5);

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(5);
        result.FirstDivergentSeq.Should().BeNull();
    }

    [Fact]
    public void Verify_TamperedMiddleEventDetail_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(5);

        // Simulate a raw on-disk edit that bypasses the append-only guard entirely: mutate the
        // stored DetailJson without recomputing EntryHash, exactly what someone with direct file
        // access to the SQLite file could do.
        events[2] = WithDetailJson(events[2], "tampered-value");

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(3);
        result.Reason.Should().Contain("EntryHash mismatch");
    }

    [Fact]
    public void Verify_TamperedEntryHash_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(4);

        events[1] = WithEntryHash(events[1], new string('f', 64));

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(2);
    }

    [Fact]
    public void Verify_IncorrectPrevHash_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(4);

        events[2] = WithPrevHash(events[2], new string('a', 64));

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(3);
        result.Reason.Should().Contain("PrevHash mismatch");
    }

    [Fact]
    public void Verify_MissingEvent_DetectsSequenceGap()
    {
        var events = BuildValidChain(5);

        events.RemoveAt(2); // remove Seq 3, leaving a gap between 2 and 4

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(4);
        result.Reason.Should().Contain("Sequence gap");
    }

    [Fact]
    public void Verify_DoesNotAssumeGenesisSeqIsOne_RejectsChainStartingElsewhere()
    {
        var operationId = Guid.NewGuid();
        var evt = BuildEvent(2, RecoveryHashChain.GenesisHash, operationId);

        var result = RecoveryChainVerifier.Verify(OwnerId, [evt]);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(2);
        result.Reason.Should().Contain("Sequence gap");
    }
}
