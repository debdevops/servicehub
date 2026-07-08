using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public class AutoReplayExecutorTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IMessageOperationsService> _messageOperations = new();
    private readonly Mock<ILogger<AutoReplayExecutor>> _logger = new();
    private readonly AutoReplayExecutor _executor;

    public AutoReplayExecutorTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _executor = new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, _logger.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMessage CreateMessage(
        long seq = 1,
        CloudProviderType provider = CloudProviderType.Azure,
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue,
        string entityName = "test-queue",
        string? topicName = null)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}", SequenceNumber = seq, BodyHash = $"hash-{seq}",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = entityName,
            EntityType = entityType,
            TopicName = topicName,
            CloudProvider = provider,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 5, MessageSize = 100,
            Status = DlqMessageStatus.Active
        };
        _dbContext.DlqMessages.Add(msg);
        _dbContext.SaveChanges();
        return msg;
    }

    private AutoReplayRule CreateRule(string name = "Test Rule", int maxPerHour = 100)
    {
        var rule = new AutoReplayRule
        {
            Name = name,
            OwnerId = TestConstants.TestOwnerId,
            Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = maxPerHour
        };
        _dbContext.AutoReplayRules.Add(rule);
        _dbContext.SaveChanges();
        return rule;
    }

    // ── Constructor ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new AutoReplayExecutor(null!, _messageOperations.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullMessageOperations_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageOperations");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, _messageOperations.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── CanReplayAsync ──────────────────────────────────────

    [Fact]
    public async Task CanReplay_RuleNotFound_ReturnsFalse()
    {
        var result = await _executor.CanReplayAsync(999);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanReplay_UnderLimit_ReturnsTrue()
    {
        var rule = CreateRule(maxPerHour: 100);
        var result = await _executor.CanReplayAsync(rule.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReplay_AtLimit_ReturnsFalse()
    {
        var rule = CreateRule(maxPerHour: 2);
        var msg = CreateMessage(1);

        // Add 2 replay histories within the last hour
        for (int i = 0; i < 2; i++)
        {
            _dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = msg.Id, RuleId = rule.Id,
                ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReplayedBy = "test", ReplayStrategy = "original",
                ReplayedToEntity = "q", OutcomeStatus = "Success"
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _executor.CanReplayAsync(rule.Id);
        result.Should().BeFalse();
    }

    // ── ExecuteAsync ────────────────────────────────────────

    [Fact]
    public async Task Execute_RateLimited_ReturnsFailure()
    {
        var rule = CreateRule(maxPerHour: 0); // 0 limit = always rate limited
        var msg = CreateMessage(1);
        var action = new RuleAction();

        // Add a replay to trigger limit
        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = msg.Id, RuleId = rule.Id,
            ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplayedBy = "test", ReplayStrategy = "original",
            ReplayedToEntity = "q", OutcomeStatus = "Success"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _executor.ExecuteAsync(msg, rule, action);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReplaySucceeds_UpdatesMessageAndRecordsHistory()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsSuccess.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
        msg.ReplaySuccess.Should().BeTrue();
        rule.SuccessCount.Should().Be(1);
        rule.MatchCount.Should().Be(1);

        var history = _dbContext.ReplayHistories.Single();
        history.DlqMessageId.Should().Be(msg.Id);
        history.RuleId.Should().Be(rule.Id);
        history.OutcomeStatus.Should().Be("Success");
        history.ReplayedToEntity.Should().Be("test-queue");
    }

    [Fact]
    public async Task Execute_ReplayFails_MarksMessageReplayFailedAndRecordsHistory()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.NotFound("NS_NOT_FOUND", "Namespace not found")));

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsFailure.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.ReplayFailed);
        msg.ReplaySuccess.Should().BeFalse();
        rule.SuccessCount.Should().Be(0);
        rule.MatchCount.Should().Be(1);

        var history = _dbContext.ReplayHistories.Single();
        history.OutcomeStatus.Should().Be("Failed");
        history.ErrorDetails.Should().Be("Namespace not found");
    }

    [Fact]
    public async Task Execute_ReplayThrows_RecordsErrorHistoryAndReturnsFailure()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsFailure.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.ReplayFailed);

        var history = _dbContext.ReplayHistories.Single();
        history.OutcomeStatus.Should().Be("Error");
        history.ErrorDetails.Should().Be("boom");
    }

    [Fact]
    public async Task Execute_TargetEntityOverride_ReplaysToAlternateEntity()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction { TargetEntity = "retry-queue" };

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "retry-queue", null, msg.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsSuccess.Should().BeTrue();
        var history = _dbContext.ReplayHistories.Single();
        history.ReplayedToEntity.Should().Be("retry-queue");
        history.ReplayStrategy.Should().Be("alternate-entity");
    }

    [Fact]
    public async Task Execute_SubscriptionMessage_ExtractsSubscriptionNameFromEntityPath()
    {
        var rule = CreateRule();
        var msg = CreateMessage(
            1,
            entityType: ServiceBusEntityType.Subscription,
            entityName: "orders-topic/subscriptions/orders-sub",
            topicName: "orders-topic");
        var action = new RuleAction();

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "orders-topic", "orders-sub", msg.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsSuccess.Should().BeTrue();
        _messageOperations.Verify(
            m => m.ReplayMessageAsync(msg.NamespaceId, "orders-topic", "orders-sub", msg.SequenceNumber, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task Execute_NonAzureProviderMessage_ReplaysViaMessageOperations(CloudProviderType provider)
    {
        var rule = CreateRule();
        var msg = CreateMessage(1, provider: provider);
        var action = new RuleAction();

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _executor.ExecuteAsync(msg, rule, action);

        result.IsSuccess.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
        _messageOperations.Verify(
            m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
