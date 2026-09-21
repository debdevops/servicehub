using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class ApprovalQueueServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly ApprovalQueueService _sut;

    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    public ApprovalQueueServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _sut = new ApprovalQueueService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor() => new("rule-engine", RecoveryActorKind.Automation);

    private async Task<AutoReplayRule> AddRuleAsync(string name = "Retry transient failures", string ownerId = OwnerA)
    {
        var rule = new AutoReplayRule
        {
            Name = name,
            OwnerId = ownerId,
            ConditionsJson = "[]",
            ActionsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    private async Task<DlqMessage> AddDlqMessageAsync(
        long sequenceNumber = 1,
        DlqMessageStatus status = DlqMessageStatus.Active,
        string entityName = "orders-dlq",
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue,
        string? topicName = null,
        string ownerId = OwnerA)
    {
        var message = new DlqMessage
        {
            MessageId = $"msg-{sequenceNumber}",
            SequenceNumber = sequenceNumber,
            BodyHash = $"hash-{sequenceNumber}",
            NamespaceId = NamespaceId,
            OwnerId = ownerId,
            EntityName = entityName,
            EntityType = entityType,
            TopicName = topicName,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
        };
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync();
        return message;
    }

    private async Task<RecoveryOperation> OpenOperationAsync(
        RecoveryTrigger trigger, long? sourceRuleId, string ownerId = OwnerA)
    {
        var result = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = trigger,
            Actor = Actor(),
            ScopeDescription = "test rule scope",
            SourceRuleId = sourceRuleId,
            TargetCount = 1,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> DeclineAsync(
        RecoveryOperation operation, DlqMessage message, string reasonCode, int? matchedCount = null,
        string? entityTypeSnapshot = null, string? topicNameSnapshot = null, string? entityNameSnapshot = null,
        long? sequenceNumberOverride = null, bool omitSequenceNumber = false)
    {
        var detail = JsonSerializer.Serialize(new { reasonCode, matchedCount });

        var result = await _ledger.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = operation.OwnerId,
                Actor = Actor(),
                DlqMessageId = message.Id,
                NamespaceId = NamespaceId,
                EntityNameSnapshot = entityNameSnapshot ?? message.EntityName,
                EntityTypeSnapshot = entityTypeSnapshot ?? message.EntityType.ToString(),
                TopicNameSnapshot = topicNameSnapshot ?? message.TopicName,
                SourceSequenceNumberSnapshot = omitSequenceNumber ? null : sequenceNumberOverride ?? message.SequenceNumber,
                BodyHash = message.BodyHash,
                TargetEntity = message.EntityName,
            },
            reasonCode,
            detail);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task ReturnsAutoRuleDeclinedEntry_WithSequenceAndReason()
    {
        var rule = await AddRuleAsync("Retry transient failures");
        var message = await AddDlqMessageAsync(sequenceNumber: 42);
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id);
        await DeclineAsync(operation, message, "AUTONOMY_GRANT_INSUFFICIENT", matchedCount: 3);

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().ContainSingle();
        var entry = results[0];
        entry.RuleId.Should().Be(rule.Id);
        entry.RuleName.Should().Be("Retry transient failures");
        entry.SequenceNumber.Should().Be(42);
        entry.EntityName.Should().Be("orders-dlq");
        entry.SubscriptionName.Should().BeNull();
        entry.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
        entry.MatchedCount.Should().Be(3);
        entry.NamespaceId.Should().Be(NamespaceId);
    }

    [Fact]
    public async Task ExcludesEntries_DeclinedFromManualTrigger()
    {
        var message = await AddDlqMessageAsync();
        var operation = await OpenOperationAsync(RecoveryTrigger.Manual, sourceRuleId: null);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED");

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludesEntries_WhoseDlqMessageIsNoLongerActive()
    {
        var rule = await AddRuleAsync();
        var message = await AddDlqMessageAsync(status: DlqMessageStatus.Replayed);
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED");

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludesEntries_FromAnotherOwner()
    {
        var rule = await AddRuleAsync(ownerId: OwnerB);
        var message = await AddDlqMessageAsync(ownerId: OwnerB);
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id, ownerId: OwnerB);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED");

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvesSubscriptionEntityAndSubscriptionNameSeparately()
    {
        var rule = await AddRuleAsync();
        var message = await AddDlqMessageAsync(
            entityType: ServiceBusEntityType.Subscription,
            entityName: "orders-topic/subscriptions/billing-sub",
            topicName: "orders-topic");
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED");

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().ContainSingle();
        results[0].EntityName.Should().Be("orders-topic");
        results[0].SubscriptionName.Should().Be("billing-sub");
    }

    [Fact]
    public async Task ExcludesEntries_MissingSequenceNumber()
    {
        var rule = await AddRuleAsync();
        var message = await AddDlqMessageAsync();
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED", omitSequenceNumber: true);

        var results = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: null, limit: 100);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FiltersByNamespaceId()
    {
        var rule = await AddRuleAsync();
        var message = await AddDlqMessageAsync();
        var operation = await OpenOperationAsync(RecoveryTrigger.AutoRule, rule.Id);
        await DeclineAsync(operation, message, "RECURRENCE_CAP_EXCEEDED");

        var otherNamespaceResults = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: Guid.NewGuid(), limit: 100);
        var matchingNamespaceResults = await _sut.GetPendingApprovalsAsync(OwnerA, namespaceId: NamespaceId, limit: 100);

        otherNamespaceResults.Should().BeEmpty();
        matchingNamespaceResults.Should().ContainSingle();
    }
}
