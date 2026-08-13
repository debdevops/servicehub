using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryLedgerAppendOnlyGuardTests : IDisposable
{
    private readonly DlqDbContext _dbContext;

    public RecoveryLedgerAppendOnlyGuardTests()
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

    private RecoveryOperation SeedOperation()
    {
        var operation = new RecoveryOperation
        {
            OwnerId = "owner-a",
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            ActorIdentity = "test",
            ActorKind = RecoveryActorKind.User,
            ScopeDescription = "entity=orders-dlq",
            ServiceVersion = "test",
            OpenedAt = DateTimeOffset.UtcNow,
            TargetCount = 1,
        };
        _dbContext.RecoveryOperations.Add(operation);
        _dbContext.SaveChanges();
        return operation;
    }

    private RecoveryLedgerEntry SeedEntry(Guid operationId)
    {
        var entry = new RecoveryLedgerEntry
        {
            OperationId = operationId,
            OwnerId = "owner-a",
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
            State = RecoveryEntryState.Executing,
        };
        _dbContext.RecoveryLedgerEntries.Add(entry);
        _dbContext.SaveChanges();
        return entry;
    }

    private RecoveryEvent SeedEvent(Guid operationId, Guid? entryId)
    {
        var evt = new RecoveryEvent
        {
            OwnerId = "owner-a",
            Seq = 1,
            EntryId = entryId,
            OperationId = operationId,
            EventType = RecoveryEventType.OperationOpened,
            OccurredAt = DateTimeOffset.UtcNow,
            ActorIdentity = "test",
            ActorKind = RecoveryActorKind.User,
            PrevHash = new string('0', 64),
            EntryHash = new string('1', 64),
            SchemaVersion = 1,
        };
        _dbContext.RecoveryEvents.Add(evt);
        _dbContext.SaveChanges();
        return evt;
    }

    [Fact]
    public void SaveChanges_ModifyRecoveryOperation_Throws()
    {
        var operation = SeedOperation();
        // RecoveryOperation's properties are init-only, so normal C# code can't even compile an
        // attempt to mutate one after construction — that's the first layer of protection. This
        // goes through EF's ChangeTracker property API instead, which bypasses the init accessor
        // the same way an ORM-level bug or a raw update could, to prove the runtime guard (the
        // second layer) independently catches it too.
        _dbContext.Entry(operation).Property(nameof(RecoveryOperation.ScopeDescription)).CurrentValue = "entity=changed";

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryOperation*immutable*");
    }

    [Fact]
    public void SaveChanges_DeleteRecoveryOperation_Throws()
    {
        var operation = SeedOperation();
        _dbContext.RecoveryOperations.Remove(operation);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryOperation*");
    }

    [Fact]
    public void SaveChanges_ModifyRecoveryEvent_Throws()
    {
        var operation = SeedOperation();
        var evt = SeedEvent(operation.Id, null);
        _dbContext.Entry(evt).Property(nameof(RecoveryEvent.DetailJson)).CurrentValue = "tampered";

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryEvent*append-only*");
    }

    [Fact]
    public void SaveChanges_DeleteRecoveryEvent_Throws()
    {
        var operation = SeedOperation();
        var evt = SeedEvent(operation.Id, null);
        _dbContext.RecoveryEvents.Remove(evt);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryEvent*");
    }

    [Fact]
    public void SaveChanges_DeleteRecoveryLedgerEntry_Throws()
    {
        var operation = SeedOperation();
        var entry = SeedEntry(operation.Id);
        _dbContext.RecoveryLedgerEntries.Remove(entry);

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryLedgerEntry*");
    }

    [Fact]
    public void SaveChanges_ModifyRecoveryLedgerEntryImmutableProperty_Throws()
    {
        var operation = SeedOperation();
        var entry = SeedEntry(operation.Id);
        _dbContext.Entry(entry).Property(nameof(RecoveryLedgerEntry.BodyHash)).CurrentValue = "changed-hash";

        var act = () => _dbContext.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*BodyHash*");
    }

    [Fact]
    public void SaveChanges_ModifyRecoveryLedgerEntryMutableProjectionOnly_Succeeds()
    {
        var operation = SeedOperation();
        var entry = SeedEntry(operation.Id);

        entry.State = RecoveryEntryState.Observing;
        entry.Disposition = null;
        entry.LastEventSeq = 2;
        entry.ObservationWindowEndsAt = DateTimeOffset.UtcNow.AddHours(24);

        var act = () => _dbContext.SaveChanges();

        act.Should().NotThrow();

        _dbContext.Entry(entry).Reload();
        entry.State.Should().Be(RecoveryEntryState.Observing);
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
        var operation = SeedOperation();
        _dbContext.Entry(operation).Property(nameof(RecoveryOperation.Reason)).CurrentValue = "changed after insert";

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

        act.Should().Throw<InvalidOperationException>().WithMessage("*RecoveryOperation*");
    }

    [Fact]
    public void MutableEntryProperties_ClassifiesEveryPublicPropertyOfRecoveryLedgerEntry()
    {
        var allPropertyNames = typeof(RecoveryLedgerEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var unclassified = allPropertyNames
            .Except(RecoveryLedgerAppendOnlyGuard.MutableEntryProperties)
            .Except(ExpectedImmutableProperties)
            .ToList();

        unclassified.Should().BeEmpty(
            "every RecoveryLedgerEntry property must be a conscious choice: either in " +
            "RecoveryLedgerAppendOnlyGuard.MutableEntryProperties (writable after insert) or in " +
            $"{nameof(ExpectedImmutableProperties)} above (fixed at insert) — a newly added " +
            "property landing in neither means that decision was never made");
    }

    private static readonly string[] ExpectedImmutableProperties =
    [
        nameof(RecoveryLedgerEntry.Id),
        nameof(RecoveryLedgerEntry.OperationId),
        nameof(RecoveryLedgerEntry.OwnerId),
        nameof(RecoveryLedgerEntry.DlqMessageId),
        nameof(RecoveryLedgerEntry.NamespaceId),
        nameof(RecoveryLedgerEntry.NamespaceNameSnapshot),
        nameof(RecoveryLedgerEntry.ProviderSnapshot),
        nameof(RecoveryLedgerEntry.EnvironmentSnapshot),
        nameof(RecoveryLedgerEntry.EntityNameSnapshot),
        nameof(RecoveryLedgerEntry.EntityTypeSnapshot),
        nameof(RecoveryLedgerEntry.TopicNameSnapshot),
        nameof(RecoveryLedgerEntry.SourceMessageIdSnapshot),
        nameof(RecoveryLedgerEntry.SourceSequenceNumberSnapshot),
        nameof(RecoveryLedgerEntry.BodyHash),
        nameof(RecoveryLedgerEntry.FailureCategorySnapshot),
        nameof(RecoveryLedgerEntry.DeadLetterReasonSnapshot),
        nameof(RecoveryLedgerEntry.SignatureHashSnapshot),
        nameof(RecoveryLedgerEntry.TargetEntity),
        nameof(RecoveryLedgerEntry.BegunAt),
    ];
}
