using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryChainVerifierTests
{
    private const string OwnerId = "owner-a";

    private static RecoveryEvent BuildEvent(
        long seq, string prevHash, Guid operationId, string? detailJson = null, int schemaVersion = 1)
    {
        var id = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        const RecoveryEventType eventType = RecoveryEventType.OperatorNote;
        const string actorIdentity = "test-actor";
        const RecoveryActorKind actorKind = RecoveryActorKind.User;

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

    [Fact]
    public void Verify_ChainSpanningTwoSchemaVersions_IsValid()
    {
        // Roadmap next-chapter M5.3: "Every RecoveryEvent carries a SchemaVersion... nothing yet
        // proves the verifier can validate a chain written before an upgrade." This builds a
        // fixture chain whose earlier events were written at SchemaVersion 1 (a pre-upgrade
        // deployment) and whose later events were written at SchemaVersion 2 (post-upgrade),
        // exactly as a real chain would look if a future migration ever bumped the constant — and
        // proves the verifier, which takes SchemaVersion as a canonical hashed field rather than
        // branching on its value, validates straight across the seam.
        var operationId = Guid.NewGuid();
        var events = new List<RecoveryEvent>();
        var prevHash = RecoveryHashChain.GenesisHash;

        for (var seq = 1; seq <= 3; seq++)
        {
            var evt = BuildEvent(seq, prevHash, operationId, $"pre-upgrade-{seq}", schemaVersion: 1);
            events.Add(evt);
            prevHash = evt.EntryHash;
        }

        for (var seq = 4; seq <= 6; seq++)
        {
            var evt = BuildEvent(seq, prevHash, operationId, $"post-upgrade-{seq}", schemaVersion: 2);
            events.Add(evt);
            prevHash = evt.EntryHash;
        }

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(6);
        events.Take(3).Should().OnlyContain(e => e.SchemaVersion == 1);
        events.Skip(3).Should().OnlyContain(e => e.SchemaVersion == 2);
    }

    [Fact]
    public void Verify_TamperedSchemaVersionAfterUpgrade_DetectsDivergence()
    {
        // SchemaVersion is a canonical hashed field (RecoveryHashChain.ComputeEntryHash), not
        // decorative metadata — silently relabeling an old event's schema version after the fact
        // must be caught exactly like any other tamper.
        var operationId = Guid.NewGuid();
        var evt1 = BuildEvent(1, RecoveryHashChain.GenesisHash, operationId, "v1", schemaVersion: 1);
        var events = new List<RecoveryEvent> { evt1 };

        var tampered = new RecoveryEvent
        {
            Id = evt1.Id,
            OwnerId = evt1.OwnerId,
            Seq = evt1.Seq,
            EntryId = evt1.EntryId,
            OperationId = evt1.OperationId,
            EventType = evt1.EventType,
            OccurredAt = evt1.OccurredAt,
            ActorIdentity = evt1.ActorIdentity,
            ActorKind = evt1.ActorKind,
            DetailJson = evt1.DetailJson,
            PrevHash = evt1.PrevHash,
            EntryHash = evt1.EntryHash,
            SchemaVersion = 2,
        };
        events[0] = tampered;

        var result = RecoveryChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("EntryHash mismatch");
    }
}
