using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

public sealed class PlaybookLedgerAppendOnlyGuardTests : IDisposable
{
    private readonly DlqDbContext _dbContext;

    public PlaybookLedgerAppendOnlyGuardTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private ServiceHub.Core.Entities.PlaybookEntry SeedEntry()
    {
        var entry = new ServiceHub.Core.Entities.PlaybookEntry
        {
            OwnerId = "owner-a",
            PillarKind = PillarKind.Investigate,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = "{}",
            ProposalJson = "{}",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "test",
            ProposerKind = PlaybookActorKind.System,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
        _dbContext.PlaybookEntries.Add(entry);
        _dbContext.SaveChanges();
        return entry;
    }

    private ServiceHub.Core.Entities.PlaybookEvent SeedEvent(Guid entryId)
    {
        var evt = new ServiceHub.Core.Entities.PlaybookEvent
        {
            OwnerId = "owner-a",
            Seq = 1,
            EntryId = entryId,
            EventType = PlaybookEventType.Proposed,
            OccurredAt = DateTimeOffset.UtcNow,
            ActorIdentity = "test",
            ActorKind = PlaybookActorKind.System,
            PrevHash = new string('0', 64),
            EntryHash = new string('1', 64),
            SchemaVersion = 1,
        };
        _dbContext.PlaybookEvents.Add(evt);
        _dbContext.SaveChanges();
        return evt;
    }

    [Fact]
    public void SaveChanges_DeletePlaybookEntry_Throws()
    {
        var entry = SeedEntry();
        _dbContext.PlaybookEntries.Remove(entry);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PlaybookEntry*");
    }

    [Fact]
    public void SaveChanges_ModifyPlaybookEntryImmutableProperty_Throws()
    {
        var entry = SeedEntry();
        _dbContext.Entry(entry).Property(nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposalJson)).CurrentValue = "{\"tampered\":true}";

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ProposalJson*");
    }

    [Fact]
    public void SaveChanges_ModifyPlaybookEntryMutableProjectionOnly_Succeeds()
    {
        var entry = SeedEntry();

        entry.State = PlaybookEntryState.UnderReview;
        entry.LastEventSeq = 2;

        var act = () => _dbContext.SaveChanges();

        act.Should().NotThrow();
    }

    [Fact]
    public void SaveChanges_DeletePlaybookEvent_Throws()
    {
        var entry = SeedEntry();
        var evt = SeedEvent(entry.Id);
        _dbContext.PlaybookEvents.Remove(evt);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PlaybookEvent*");
    }

    [Fact]
    public void SaveChanges_ModifyPlaybookEvent_Throws()
    {
        var entry = SeedEntry();
        var evt = SeedEvent(entry.Id);
        _dbContext.Entry(evt).Property(nameof(ServiceHub.Core.Entities.PlaybookEvent.DetailJson)).CurrentValue = "tampered";

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PlaybookEvent*append-only*");
    }

    [Fact]
    public void SaveChanges_UnrelatedDlqMessageUpdate_IsUnaffected()
    {
        var message = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = "owner-a",
            EntityName = "orders-dlq",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
        };
        _dbContext.DlqMessages.Add(message);
        _dbContext.SaveChanges();

        message.UserNotes = "updated notes";

        var act = () => _dbContext.SaveChanges();

        act.Should().NotThrow();
    }

    [Fact]
    public void SaveChanges_MixedBatch_UnrelatedEntityChangeDoesNotBypassLedgerEnforcement()
    {
        var entry = SeedEntry();
        _dbContext.Entry(entry).Property(nameof(ServiceHub.Core.Entities.PlaybookEntry.EvidenceRefJson)).CurrentValue = "changed after insert";

        var message = new DlqMessage
        {
            MessageId = "msg-2",
            SequenceNumber = 2,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = "owner-a",
            EntityName = "orders-dlq",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
        };
        _dbContext.DlqMessages.Add(message);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PlaybookEntry*");
    }

    [Fact]
    public void MutableEntryProperties_ClassifiesEveryPublicPropertyOfPlaybookEntry()
    {
        var allPropertyNames = typeof(ServiceHub.Core.Entities.PlaybookEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var unclassified = allPropertyNames
            .Except(ServiceHub.Infrastructure.Persistence.PlaybookLedgerAppendOnlyGuard.MutableEntryProperties)
            .Except(ExpectedImmutableProperties)
            .ToList();

        unclassified.Should().BeEmpty(
            "every PlaybookEntry property must be a conscious choice: either in " +
            "PlaybookLedgerAppendOnlyGuard.MutableEntryProperties (writable after insert) or in " +
            $"{nameof(ExpectedImmutableProperties)} above (fixed at insert) — a newly added " +
            "property landing in neither means that decision was never made");
    }

    private static readonly string[] ExpectedImmutableProperties =
    [
        nameof(ServiceHub.Core.Entities.PlaybookEntry.Id),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.OwnerId),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.PillarKind),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposalKind),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.EvidenceRefJson),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposalJson),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposedAt),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposerIdentity),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProposerKind),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.SignatureHashSnapshot),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.NamespaceId),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.NamespaceNameSnapshot),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ProviderSnapshot),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.EnvironmentSnapshot),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.RelatedRecoveryOperationId),
        nameof(ServiceHub.Core.Entities.PlaybookEntry.ExpiresAt),
    ];
}
