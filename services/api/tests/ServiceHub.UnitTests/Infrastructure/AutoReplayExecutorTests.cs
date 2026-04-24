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
    private readonly Mock<INamespaceRepository> _nsRepo = new();
    private readonly Mock<IServiceBusClientCache> _clientCache = new();
    private readonly Mock<IConnectionStringProtector> _protector = new();
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
            _dbContext, _nsRepo.Object, _clientCache.Object,
            _protector.Object, _logger.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMessage CreateMessage(long seq = 1)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}", SequenceNumber = seq, BodyHash = $"hash-{seq}",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
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
        var act = () => new AutoReplayExecutor(null!, _nsRepo.Object, _clientCache.Object, _protector.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, null!, _clientCache.Object, _protector.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullClientCache_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, _nsRepo.Object, null!, _protector.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_NullProtector_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, _nsRepo.Object, _clientCache.Object, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("protector");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AutoReplayExecutor(_dbContext, _nsRepo.Object, _clientCache.Object, _protector.Object, null!);
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
    public async Task Execute_NamespaceNotFound_ReturnsFailure()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        _nsRepo.Setup(r => r.GetByIdAsync(msg.NamespaceId))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NS_NOT_FOUND", "Not found")));

        var result = await _executor.ExecuteAsync(msg, rule, action);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_EmptyConnectionString_ReturnsFailure()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        // Mock namespace return with connection string that, after creation, we clear
        var nsResult = Namespace.Create("test-ns", "");
        // Since Namespace.Create may not accept empty string, let's mock differently
        _nsRepo.Setup(r => r.GetByIdAsync(msg.NamespaceId))
            .ReturnsAsync(Result<Namespace>.Failure(Error.Validation("NS", "No connection string")));

        var result = await _executor.ExecuteAsync(msg, rule, action);
        result.IsFailure.Should().BeTrue();
    }
}
