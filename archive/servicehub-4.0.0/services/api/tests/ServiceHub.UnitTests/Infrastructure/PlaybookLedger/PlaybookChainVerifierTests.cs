using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.PlaybookLedger;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

public sealed class PlaybookChainVerifierTests
{
    private const string OwnerId = "owner-a";

    private static PlaybookEvent BuildEvent(long seq, string prevHash, Guid entryId, string? detailJson = null)
    {
        var id = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        const PlaybookEventType eventType = PlaybookEventType.Proposed;
        const string actorIdentity = "test-actor";
        const PlaybookActorKind actorKind = PlaybookActorKind.System;
        const int schemaVersion = 1;

        var hash = PlaybookHashChain.ComputeEntryHash(
            id, OwnerId, seq, entryId, eventType, occurredAt,
            actorIdentity, actorKind, detailJson, schemaVersion, prevHash);

        return new PlaybookEvent
        {
            Id = id,
            OwnerId = OwnerId,
            Seq = seq,
            EntryId = entryId,
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

    private static PlaybookEvent WithDetailJson(PlaybookEvent source, string detailJson) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = detailJson,
        PrevHash = source.PrevHash,
        EntryHash = source.EntryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static PlaybookEvent WithEntryHash(PlaybookEvent source, string entryHash) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = source.DetailJson,
        PrevHash = source.PrevHash,
        EntryHash = entryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static PlaybookEvent WithPrevHash(PlaybookEvent source, string prevHash) => new()
    {
        Id = source.Id,
        OwnerId = source.OwnerId,
        Seq = source.Seq,
        EntryId = source.EntryId,
        EventType = source.EventType,
        OccurredAt = source.OccurredAt,
        ActorIdentity = source.ActorIdentity,
        ActorKind = source.ActorKind,
        DetailJson = source.DetailJson,
        PrevHash = prevHash,
        EntryHash = source.EntryHash,
        SchemaVersion = source.SchemaVersion,
    };

    private static List<PlaybookEvent> BuildValidChain(int count)
    {
        var entryId = Guid.NewGuid();
        var events = new List<PlaybookEvent>();
        var prevHash = PlaybookHashChain.GenesisHash;

        for (var seq = 1; seq <= count; seq++)
        {
            var evt = BuildEvent(seq, prevHash, entryId, $"detail-{seq}");
            events.Add(evt);
            prevHash = evt.EntryHash;
        }

        return events;
    }

    [Fact]
    public void Verify_EmptyChain_IsValid()
    {
        var result = PlaybookChainVerifier.Verify(OwnerId, []);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(0);
        result.FirstDivergentSeq.Should().BeNull();
    }

    [Fact]
    public void Verify_ValidChain_IsValid()
    {
        var events = BuildValidChain(5);

        var result = PlaybookChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(5);
        result.FirstDivergentSeq.Should().BeNull();
    }

    [Fact]
    public void Verify_TamperedMiddleEventDetail_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(5);
        events[2] = WithDetailJson(events[2], "tampered-value");

        var result = PlaybookChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(3);
        result.Reason.Should().Contain("EntryHash mismatch");
    }

    [Fact]
    public void Verify_TamperedEntryHash_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(4);
        events[1] = WithEntryHash(events[1], new string('f', 64));

        var result = PlaybookChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(2);
    }

    [Fact]
    public void Verify_IncorrectPrevHash_DetectsDivergenceAtThatSeq()
    {
        var events = BuildValidChain(4);
        events[2] = WithPrevHash(events[2], new string('a', 64));

        var result = PlaybookChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(3);
        result.Reason.Should().Contain("PrevHash mismatch");
    }

    [Fact]
    public void Verify_MissingEvent_DetectsSequenceGap()
    {
        var events = BuildValidChain(5);
        events.RemoveAt(2);

        var result = PlaybookChainVerifier.Verify(OwnerId, events);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(4);
        result.Reason.Should().Contain("Sequence gap");
    }

    [Fact]
    public void Verify_DoesNotAssumeGenesisSeqIsOne_RejectsChainStartingElsewhere()
    {
        var evt = BuildEvent(2, PlaybookHashChain.GenesisHash, Guid.NewGuid());

        var result = PlaybookChainVerifier.Verify(OwnerId, [evt]);

        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(2);
        result.Reason.Should().Contain("Sequence gap");
    }

    [Fact]
    public void Verify_IndependentFromRecoveryHashChain_SameCanonicalInputsProduceDifferentHash()
    {
        // Guards against an accidental future refactor that merges the two chains' algorithms —
        // the two must stay cryptographically independent even if their inputs happen to align.
        var recoveryHash = ServiceHub.Infrastructure.RecoveryLedger.RecoveryHashChain.ComputeEntryHash(
            Guid.Empty, OwnerId, 1, null, Guid.Empty, ServiceHub.Core.Enums.RecoveryEventType.OperatorNote,
            DateTimeOffset.UnixEpoch, "actor", ServiceHub.Core.Enums.RecoveryActorKind.System, null, 1,
            ServiceHub.Infrastructure.RecoveryLedger.RecoveryHashChain.GenesisHash);

        var playbookHash = PlaybookHashChain.ComputeEntryHash(
            Guid.Empty, OwnerId, 1, Guid.Empty, PlaybookEventType.Proposed,
            DateTimeOffset.UnixEpoch, "actor", PlaybookActorKind.System, null, 1,
            PlaybookHashChain.GenesisHash);

        recoveryHash.Should().NotBe(playbookHash);
    }
}
