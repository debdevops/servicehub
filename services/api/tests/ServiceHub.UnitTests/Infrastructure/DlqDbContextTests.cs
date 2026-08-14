using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure;

public class DlqDbContextTests : IDisposable
{
    private readonly DlqDbContext _dbContext;

    public DlqDbContextTests()
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

    [Fact]
    public async Task DlqMessages_CanInsertAndQuery()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 5,
            MessageSize = 100,
            FailureCategory = FailureCategory.Transient,
            Status = DlqMessageStatus.Active
        };

        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.DlqMessages.FirstAsync();
        loaded.MessageId.Should().Be("msg-1");
        loaded.EntityType.Should().Be(ServiceBusEntityType.Queue);
        loaded.FailureCategory.Should().Be(FailureCategory.Transient);
        loaded.Status.Should().Be(DlqMessageStatus.Active);
        // CloudProvider defaults to Azure when not explicitly set.
        loaded.CloudProvider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task DlqMessages_PersistsCloudProvider()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-aws",
            SequenceNumber = 1,
            BodyHash = "hash-aws",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            CloudProvider = CloudProviderType.Aws,
            EntityName = "aws-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1,
            MessageSize = 50
        };

        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.DlqMessages.FirstAsync();
        loaded.CloudProvider.Should().Be(CloudProviderType.Aws);
    }

    [Fact]
    public async Task DlqMessages_EnforcesUniqueConstraint()
    {
        var nsId = Guid.NewGuid();
        var msg1 = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = nsId, OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50
        };
        var msg2 = new DlqMessage
        {
            MessageId = "msg-2", SequenceNumber = 1, BodyHash = "hash-2",
            NamespaceId = nsId, OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50
        };

        _dbContext.DlqMessages.Add(msg1);
        await _dbContext.SaveChangesAsync();

        _dbContext.DlqMessages.Add(msg2);
        var act = () => _dbContext.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AutoReplayRules_CanInsertAndQuery()
    {
        var rule = new AutoReplayRule
        {
            Name = "Test Rule",
            OwnerId = TestConstants.TestOwnerId,
            Description = "A test",
            Enabled = true,
            ConditionsJson = "[{\"field\":\"FailureCategory\",\"operator\":\"Equals\",\"value\":\"Transient\"}]",
            ActionsJson = "{\"autoReplay\":true}",
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = 100
        };

        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.AutoReplayRules.FirstAsync();
        loaded.Name.Should().Be("Test Rule");
        loaded.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task ReplayHistories_CascadeDeleteWithDlqMessage()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50
        };
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = msg.Id,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "test-user",
            ReplayStrategy = "original-entity",
            ReplayedToEntity = "q1",
            OutcomeStatus = "Success"
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.DlqMessages.Remove(msg);
        await _dbContext.SaveChangesAsync();

        var histories = await _dbContext.ReplayHistories.ToListAsync();
        histories.Should().BeEmpty();
    }

    [Fact]
    public async Task DlqMessages_DateTimeOffsetConversion_PreservesValues()
    {
        var now = DateTimeOffset.UtcNow;
        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = now,
            DetectedAtUtc = now,
            DeadLetterTimeUtc = now.AddMinutes(-5),
            ReplayedAt = now.AddMinutes(5),
            ArchivedAt = now.AddMinutes(10),
            DeliveryCount = 1, MessageSize = 50
        };

        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.DlqMessages.FirstAsync();
        // DateTimeOffset values are converted through UTC converter
        loaded.EnqueuedTimeUtc.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        loaded.DeadLetterTimeUtc.Should().NotBeNull();
        loaded.ReplayedAt.Should().NotBeNull();
        loaded.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DlqMessages_AllOptionalFieldsNullable()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50,
            DeadLetterReason = null,
            DeadLetterErrorDescription = null,
            ContentType = null,
            BodyPreview = null,
            ApplicationPropertiesJson = null,
            UserNotes = null,
            ForensicRootCause = null,
            ReplaySafety = null,
            CorrelationId = null,
            SessionId = null,
            TopicName = null
        };

        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.DlqMessages.FirstAsync();
        loaded.DeadLetterReason.Should().BeNull();
        loaded.ContentType.Should().BeNull();
        loaded.BodyPreview.Should().BeNull();
    }

    [Fact]
    public async Task AutoReplayRule_MutableProperties_CanBeUpdated()
    {
        var rule = new AutoReplayRule
        {
            Name = "Test", OwnerId = TestConstants.TestOwnerId, Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        rule.Enabled = false;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.MatchCount = 42;
        rule.SuccessCount = 30;
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.AutoReplayRules.FirstAsync();
        loaded.Enabled.Should().BeFalse();
        loaded.MatchCount.Should().Be(42);
        loaded.SuccessCount.Should().Be(30);
    }

    [Fact]
    public async Task ReplayHistory_WithRule_SetNullOnRuleDelete()
    {
        var rule = new AutoReplayRule
        {
            Name = "Test", OwnerId = TestConstants.TestOwnerId, Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AutoReplayRules.Add(rule);
        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50
        };
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = msg.Id,
            RuleId = rule.Id,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "test",
            ReplayStrategy = "original",
            ReplayedToEntity = "q1",
            OutcomeStatus = "Success"
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.AutoReplayRules.Remove(rule);
        await _dbContext.SaveChangesAsync();

        var history = await _dbContext.ReplayHistories.FirstAsync();
        history.RuleId.Should().BeNull();
    }

    // ── C2: Status as an EF Core concurrency token ─────────────────────────────

    [Fact]
    public async Task DlqMessages_SingleContextStatusUpdate_SavesSuccessfully()
    {
        // Normal single-writer usage must be unaffected by Status becoming a concurrency
        // token — only a genuine cross-context race should trigger a conflict.
        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50,
            Status = DlqMessageStatus.Active
        };
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        msg.Status = DlqMessageStatus.Replaying;
        await _dbContext.SaveChangesAsync();

        msg.Status = DlqMessageStatus.Replayed;
        msg.ReplayedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        loaded.Status.Should().Be(DlqMessageStatus.Replayed);
    }

    [Fact]
    public async Task DlqMessages_ConcurrentStatusUpdate_LoserThrowsDbUpdateConcurrencyException()
    {
        // Two independent DlqDbContext instances (standing in for two replay workers, e.g.
        // bulk-replay and signature-replay) over the SAME underlying database — a
        // shared-cache SQLite connection is required for a second context to see the first
        // context's uncommitted-to-disk data, unlike the private ":memory:" DB the other
        // tests in this class use.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;

        using var contextA = new DlqDbContext(options);
        contextA.Database.EnsureCreated();

        var namespaceId = Guid.NewGuid();
        var msg = new DlqMessage
        {
            MessageId = "msg-race", SequenceNumber = 1, BodyHash = "hash-race",
            NamespaceId = namespaceId, OwnerId = TestConstants.TestOwnerId, EntityName = "q1",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 1, MessageSize = 50,
            Status = DlqMessageStatus.Active
        };
        contextA.DlqMessages.Add(msg);
        await contextA.SaveChangesAsync();

        using var contextB = new DlqDbContext(options);
        var msgViaB = await contextB.DlqMessages.SingleAsync(m => m.Id == msg.Id);

        // Worker B (via contextB) claims and completes the replay first.
        msgViaB.Status = DlqMessageStatus.Replaying;
        await contextB.SaveChangesAsync();
        msgViaB.Status = DlqMessageStatus.Replayed;
        msgViaB.ReplayedAt = DateTimeOffset.UtcNow;
        await contextB.SaveChangesAsync();

        // Worker A (via contextA) still holds its original, now-stale Active snapshot and
        // attempts its own claim — the loser.
        msg.Status = DlqMessageStatus.Replaying;
        var act = async () => await contextA.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Recovering (as the executors do) resyncs the loser with the real, winning state.
        await contextA.Entry(msg).ReloadAsync();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
    }

    private static AutonomyGrant NewGrant(
        string ownerId = "owner-a", string signatureHash = "sig-1",
        RecoveryOperationKind actionKind = RecoveryOperationKind.Replay,
        AutonomyLevel level = AutonomyLevel.Approve) => new()
    {
        OwnerId = ownerId,
        SignatureHash = signatureHash,
        ActionKind = actionKind,
        CurrentLevel = level,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task AutonomyGrants_CanInsertAndQuery()
    {
        _dbContext.AutonomyGrants.Add(NewGrant());
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.AutonomyGrants.FirstAsync();
        loaded.OwnerId.Should().Be("owner-a");
        loaded.SignatureHash.Should().Be("sig-1");
        loaded.ActionKind.Should().Be(RecoveryOperationKind.Replay);
        loaded.CurrentLevel.Should().Be(AutonomyLevel.Approve);
        loaded.ConcurrencyStamp.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task AutonomyGrants_EnforcesUniqueConstraintOnOwnerSignatureAction()
    {
        _dbContext.AutonomyGrants.Add(NewGrant());
        await _dbContext.SaveChangesAsync();

        // Same (OwnerId, SignatureHash, ActionKind) triple — must be rejected even though it's a
        // distinct row (different Id, different CurrentLevel).
        _dbContext.AutonomyGrants.Add(NewGrant(level: AutonomyLevel.Standing));
        var act = () => _dbContext.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AutonomyGrants_ConcurrencyStamp_ChangesOnEveryInsertAndUpdate()
    {
        var grant = NewGrant();
        _dbContext.AutonomyGrants.Add(grant);
        await _dbContext.SaveChangesAsync();
        var afterInsert = grant.ConcurrencyStamp;
        afterInsert.Should().NotBe(Guid.Empty);

        grant.CurrentLevel = AutonomyLevel.Standing;
        await _dbContext.SaveChangesAsync();
        var afterUpdate = grant.ConcurrencyStamp;

        afterUpdate.Should().NotBe(afterInsert);
    }

    /// <summary>Builds the minimal operation+event pair <c>RecordAutonomyGrantTransitionAsync</c>
    /// itself writes alongside a grant mutation, for tests that need to prove the two are written
    /// atomically without going through <c>RecoveryLedgerService</c> — whose per-owner semaphore
    /// otherwise serialises same-owner calls in-process, meaning it cannot be made to race itself
    /// here (roadmap §9.4.4's protections exist for the cross-process case that serialisation
    /// can't cover).</summary>
    private static RecoveryOperation NewGrantChangeOperation(string ownerId = "owner-a") => new()
    {
        OwnerId = ownerId,
        Kind = RecoveryOperationKind.AutonomyGrantChange,
        Trigger = RecoveryTrigger.AutonomyEvaluation,
        ActorIdentity = "System:AutonomyEvaluationWorker",
        ActorKind = RecoveryActorKind.System,
        ScopeDescription = "signature=sig-race; action=Replay",
        ServiceVersion = "test",
        OpenedAt = DateTimeOffset.UtcNow,
        TargetCount = 0,
    };

    private static RecoveryEvent NewGrantChangeEvent(Guid operationId, string ownerId, long seq) => new()
    {
        OwnerId = ownerId,
        Seq = seq,
        EntryId = null,
        OperationId = operationId,
        EventType = RecoveryEventType.AutonomyGrantPromoted,
        OccurredAt = DateTimeOffset.UtcNow,
        ActorIdentity = "System:AutonomyEvaluationWorker",
        ActorKind = RecoveryActorKind.System,
        PrevHash = new string('0', 64),
        EntryHash = new string(seq == 1 ? '1' : '2', 64),
        SchemaVersion = 1,
    };

    [Fact]
    public async Task AutonomyGrants_ConcurrentFirstCreation_LoserThrowsDbUpdateException()
    {
        // Two independent DlqDbContext instances over a shared-cache SQLite DB, standing in for
        // two AutonomyEvaluationWorker sweep cycles both deciding — for the first time — that the
        // same (OwnerId, SignatureHash, ActionKind) triple has earned a grant (roadmap §9.4.4
        // Correction, protection A). Each side also writes the operation+event pair the real
        // transition method bundles with the grant write, to prove the whole unit of work — not
        // just the grant row — rolls back together for the loser.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;

        using var contextA = new DlqDbContext(options);
        contextA.Database.EnsureCreated();
        using var contextB = new DlqDbContext(options);

        var opA = NewGrantChangeOperation();
        contextA.RecoveryOperations.Add(opA);
        contextA.RecoveryEvents.Add(NewGrantChangeEvent(opA.Id, opA.OwnerId, seq: 1));
        contextA.AutonomyGrants.Add(NewGrant(signatureHash: "sig-race", level: AutonomyLevel.Approve));

        var opB = NewGrantChangeOperation();
        contextB.RecoveryOperations.Add(opB);
        contextB.RecoveryEvents.Add(NewGrantChangeEvent(opB.Id, opB.OwnerId, seq: 1));
        contextB.AutonomyGrants.Add(NewGrant(signatureHash: "sig-race", level: AutonomyLevel.Standing));

        await contextA.SaveChangesAsync();
        var act = () => contextB.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        (await contextA.AutonomyGrants.CountAsync(g => g.SignatureHash == "sig-race")).Should().Be(1);
        // B's operation+event never committed either — the whole SaveChanges call rolled back
        // together, not just the grant insert that triggered the failure.
        (await contextA.RecoveryOperations.CountAsync(o => o.Id == opB.Id)).Should().Be(0);
        (await contextA.RecoveryEvents.CountAsync(e => e.OperationId == opB.Id)).Should().Be(0);
    }

    [Fact]
    public async Task AutonomyGrants_ConcurrentUpdate_LoserThrowsDbUpdateConcurrencyException()
    {
        // Two independent DlqDbContext instances over a shared-cache SQLite DB, standing in for
        // two AutonomyEvaluationWorker sweep cycles both mutating the same, already-existing
        // grant row (roadmap §9.4.4 Correction, protection B). Each side also writes the
        // operation+event pair the real transition method bundles with the grant mutation, to
        // prove the whole unit of work rolls back together for the loser, not just the grant row.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;

        using var contextA = new DlqDbContext(options);
        contextA.Database.EnsureCreated();

        var grant = NewGrant(signatureHash: "sig-update-race");
        contextA.AutonomyGrants.Add(grant);
        await contextA.SaveChangesAsync();

        using var contextB = new DlqDbContext(options);
        var grantViaB = await contextB.AutonomyGrants.SingleAsync(g => g.Id == grant.Id);

        // Worker B claims first and saves — its own operation+event pair too.
        var opB = NewGrantChangeOperation();
        contextB.RecoveryOperations.Add(opB);
        contextB.RecoveryEvents.Add(NewGrantChangeEvent(opB.Id, opB.OwnerId, seq: 1));
        grantViaB.CurrentLevel = AutonomyLevel.Standing;
        await contextB.SaveChangesAsync();

        // Worker A still holds its original, now-stale ConcurrencyStamp snapshot, and attempts
        // its own competing operation+event pair alongside its (losing) grant mutation.
        var opA = NewGrantChangeOperation();
        contextA.RecoveryOperations.Add(opA);
        contextA.RecoveryEvents.Add(NewGrantChangeEvent(opA.Id, opA.OwnerId, seq: 2));
        grant.CurrentLevel = AutonomyLevel.Unattended;
        var act = async () => await contextA.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await contextA.Entry(grant).ReloadAsync();
        grant.CurrentLevel.Should().Be(AutonomyLevel.Standing);
        // A's operation+event never committed either — rolled back with the losing grant update.
        (await contextB.RecoveryOperations.CountAsync(o => o.Id == opA.Id)).Should().Be(0);
        (await contextB.RecoveryEvents.CountAsync(e => e.OperationId == opA.Id)).Should().Be(0);
    }
}
