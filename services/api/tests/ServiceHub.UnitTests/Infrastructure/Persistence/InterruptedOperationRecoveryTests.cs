using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Regression pack for v3.6.0 P2-1: a process that died between committing a Replaying/Purging
/// claim and completing the provider call left the message permanently ineligible for every
/// replay path, with nothing anywhere reconciling it.
/// </summary>
public sealed class InterruptedOperationRecoveryTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Guid _namespaceId = Guid.NewGuid();

    public InterruptedOperationRecoveryTests()
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

    private DlqMessage AddMessage(DlqMessageStatus status, long seq = 1)
    {
        var message = new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = _namespaceId,
            OwnerId = "__spa__",
            EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
        };
        _dbContext.DlqMessages.Add(message);
        _dbContext.SaveChanges();
        return message;
    }

    private Task<int> RunRecoveryAsync() =>
        InterruptedOperationRecovery.ReconcileInterruptedOperationsAsync(
            _dbContext, NullLogger.Instance);

    [Fact]
    public async Task StrandedReplayingMessage_BecomesReplayFailed_AndIsEligibleAgain()
    {
        var message = AddMessage(DlqMessageStatus.Replaying);

        var recovered = await RunRecoveryAsync();

        recovered.Should().Be(1);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.ReplayFailed);

        // Eligibility for every replay path is Active or ReplayFailed — this is the assertion
        // that the message is genuinely recoverable rather than merely relabelled.
        stored.Status.Should().BeOneOf(DlqMessageStatus.Active, DlqMessageStatus.ReplayFailed);
    }

    [Fact]
    public async Task StrandedReplayingMessage_DoesNotClaimTheReplaySucceededOrFailed()
    {
        var message = AddMessage(DlqMessageStatus.Replaying);

        await RunRecoveryAsync();

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.ReplaySuccess.Should().BeNull(
            "the provider may have accepted the message before the crash — asserting either "
            + "outcome would be a claim we never observed");
    }

    [Fact]
    public async Task StrandedReplayingMessage_RecordsAnHonestReplayHistoryEntry()
    {
        var message = AddMessage(DlqMessageStatus.Replaying);

        await RunRecoveryAsync();

        var history = await _dbContext.ReplayHistories.AsNoTracking()
            .Where(h => h.DlqMessageId == message.Id)
            .ToListAsync();

        history.Should().ContainSingle();
        history[0].OutcomeStatus.Should().Be("Interrupted",
            "neither 'Success' nor 'Failed' is true, and claiming either would be dishonest");
        history[0].ErrorDetails.Should().Contain("verify");
        history[0].ReplayedBy.Should().Be("startup-recovery");
    }

    [Fact]
    public async Task StrandedPurgingMessage_BecomesActive_NotDiscarded()
    {
        var message = AddMessage(DlqMessageStatus.Purging);

        var recovered = await RunRecoveryAsync();

        recovered.Should().Be(1);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.Active,
            "Discarded would assert the message left the DLQ, which the claim never established");
    }

    [Fact]
    public async Task StrandedPurgingMessage_DoesNotWriteAReplayHistoryEntry()
    {
        var message = AddMessage(DlqMessageStatus.Purging);

        await RunRecoveryAsync();

        var history = await _dbContext.ReplayHistories.AsNoTracking()
            .Where(h => h.DlqMessageId == message.Id)
            .ToListAsync();

        history.Should().BeEmpty("an interrupted purge is not a replay attempt");
    }

    [Theory]
    [InlineData(DlqMessageStatus.Active)]
    [InlineData(DlqMessageStatus.Replayed)]
    [InlineData(DlqMessageStatus.ReplayFailed)]
    [InlineData(DlqMessageStatus.Discarded)]
    [InlineData(DlqMessageStatus.Archived)]
    [InlineData(DlqMessageStatus.Resolved)]
    public async Task NonClaimedStatuses_AreLeftCompletelyUntouched(DlqMessageStatus status)
    {
        var message = AddMessage(status);

        var recovered = await RunRecoveryAsync();

        recovered.Should().Be(0);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(status);
        (await _dbContext.ReplayHistories.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MixedBatch_RecoversEachClaimStateAppropriately()
    {
        var replaying = AddMessage(DlqMessageStatus.Replaying, seq: 1);
        var purging = AddMessage(DlqMessageStatus.Purging, seq: 2);
        var active = AddMessage(DlqMessageStatus.Active, seq: 3);

        var recovered = await RunRecoveryAsync();

        recovered.Should().Be(2);

        var stored = await _dbContext.DlqMessages.AsNoTracking().ToListAsync();
        stored.First(m => m.Id == replaying.Id).Status.Should().Be(DlqMessageStatus.ReplayFailed);
        stored.First(m => m.Id == purging.Id).Status.Should().Be(DlqMessageStatus.Active);
        stored.First(m => m.Id == active.Id).Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task IsIdempotent_ASecondRunFindsNothingToDo()
    {
        AddMessage(DlqMessageStatus.Replaying);

        (await RunRecoveryAsync()).Should().Be(1);
        (await RunRecoveryAsync()).Should().Be(0,
            "a restart loop must not accumulate a replay-history entry per boot");

        (await _dbContext.ReplayHistories.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmptyDatabase_IsANoOp()
    {
        (await RunRecoveryAsync()).Should().Be(0);
    }
}
