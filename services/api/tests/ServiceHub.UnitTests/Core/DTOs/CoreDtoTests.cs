using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.DTOs;

public class CoreDtoTests
{
    #region Response DTOs

    [Fact]
    public void NamespaceResponse_ShouldCreateWithAllProperties()
    {
        var id = Guid.NewGuid();
        var response = new NamespaceResponse(
            Id: id,
            Name: "test-ns",
            DisplayName: "Test NS",
            Description: "A test namespace",
            AuthType: ConnectionAuthType.ConnectionString,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            ModifiedAt: DateTimeOffset.UtcNow,
            LastConnectionTestAt: DateTimeOffset.UtcNow,
            LastConnectionTestSucceeded: true,
            HasListenPermission: true,
            HasSendPermission: true,
            HasManagePermission: false,
            Environment: EnvironmentType.Dev);

        response.Id.Should().Be(id);
        response.Name.Should().Be("test-ns");
        response.DisplayName.Should().Be("Test NS");
        response.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        response.IsActive.Should().BeTrue();
        response.HasListenPermission.Should().BeTrue();
        response.HasManagePermission.Should().BeFalse();
    }

    [Fact]
    public void MessageResponse_ShouldCreateWithAllProperties()
    {
        var props = new Dictionary<string, object> { ["key"] = "value" };
        var response = new MessageResponse(
            MessageId: "msg-1",
            SequenceNumber: 42,
            Body: "{\"key\":\"value\"}",
            ContentType: "application/json",
            CorrelationId: "corr-1",
            SessionId: "sess-1",
            PartitionKey: "pk-1",
            Subject: "test-subject",
            ReplyTo: "reply-queue",
            ReplyToSessionId: "reply-sess",
            To: "dest-queue",
            TimeToLive: TimeSpan.FromHours(1),
            ScheduledEnqueueTime: DateTimeOffset.UtcNow.AddMinutes(5),
            EnqueuedTime: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            LockedUntil: null,
            DeliveryCount: 3,
            State: MessageState.Active,
            DeadLetterSource: null,
            DeadLetterReason: null,
            DeadLetterErrorDescription: null,
            ApplicationProperties: props,
            SizeInBytes: 256,
            EntityName: "my-queue",
            SubscriptionName: null,
            IsFromDeadLetter: false);

        response.MessageId.Should().Be("msg-1");
        response.SequenceNumber.Should().Be(42);
        response.Body.Should().Contain("key");
        response.ContentType.Should().Be("application/json");
        response.DeliveryCount.Should().Be(3);
        response.State.Should().Be(MessageState.Active);
        response.SizeInBytes.Should().Be(256);
        response.IsFromDeadLetter.Should().BeFalse();
    }

    [Fact]
    public void QueueRuntimePropertiesDto_ShouldCreateWithAllProperties()
    {
        var dto = new QueueRuntimePropertiesDto(
            Name: "test-queue",
            ActiveMessageCount: 100,
            DeadLetterMessageCount: 5,
            ScheduledMessageCount: 2,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            SizeInBytes: 4096,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            RequiresDuplicateDetection: true,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue);

        dto.Name.Should().Be("test-queue");
        dto.ActiveMessageCount.Should().Be(100);
        dto.DeadLetterMessageCount.Should().Be(5);
        dto.RequiresDuplicateDetection.Should().BeTrue();
        dto.MaxDeliveryCount.Should().Be(10);
    }

    [Fact]
    public void TopicRuntimePropertiesDto_ShouldCreateWithAllProperties()
    {
        var dto = new TopicRuntimePropertiesDto(
            Name: "test-topic",
            SubscriptionCount: 5,
            SizeInBytes: 8192,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresDuplicateDetection: false,
            EnablePartitioning: true,
            EnableBatchedOperations: true,
            SupportOrdering: false,
            MaxSizeInMegabytes: 2048,
            DefaultMessageTimeToLive: TimeSpan.FromDays(7),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));

        dto.Name.Should().Be("test-topic");
        dto.SubscriptionCount.Should().Be(5);
        dto.EnablePartitioning.Should().BeTrue();
    }

    [Fact]
    public void SubscriptionRuntimePropertiesDto_ShouldCreateWithAllProperties()
    {
        var dto = new SubscriptionRuntimePropertiesDto(
            Name: "test-sub",
            TopicName: "test-topic",
            ActiveMessageCount: 25,
            DeadLetterMessageCount: 3,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-14),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: true,
            EnableDeadLetteringOnFilterEvaluationExceptions: false,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(60),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: "forward-queue",
            ForwardDeadLetteredMessagesTo: null);

        dto.Name.Should().Be("test-sub");
        dto.TopicName.Should().Be("test-topic");
        dto.ActiveMessageCount.Should().Be(25);
        dto.ForwardTo.Should().Be("forward-queue");
        dto.EnableDeadLetteringOnMessageExpiration.Should().BeTrue();
    }

    [Fact]
    public void PaginatedResponse_ShouldCreateWithAllProperties()
    {
        var items = new List<string> { "a", "b", "c" };
        var response = new PaginatedResponse<string>(
            Items: items,
            TotalCount: 10,
            Page: 1,
            PageSize: 3,
            HasNextPage: true,
            HasPreviousPage: false);

        response.Items.Should().HaveCount(3);
        response.TotalCount.Should().Be(10);
        response.Page.Should().Be(1);
        response.HasNextPage.Should().BeTrue();
        response.HasPreviousPage.Should().BeFalse();
    }

    #endregion

    #region DLQ Response DTOs

    [Fact]
    public void DlqHistoryResponse_ShouldCreateWithAllProperties()
    {
        var response = new DlqHistoryResponse(
            Id: 1,
            MessageId: "msg-1",
            SequenceNumber: 100,
            BodyHash: "abc123",
            NamespaceId: Guid.NewGuid(),
            EntityName: "test-queue",
            EntityType: "Queue",
            EnqueuedTimeUtc: DateTimeOffset.UtcNow.AddHours(-2),
            DeadLetterTimeUtc: DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc: DateTimeOffset.UtcNow,
            DeadLetterReason: "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription: "Delivery count exceeded",
            DeliveryCount: 10,
            ContentType: "application/json",
            MessageSize: 512,
            BodyPreview: "{\"key\":\"value\"}",
            FailureCategory: "DeliveryFailure",
            CategoryConfidence: 0.95,
            Status: "Active",
            ReplayedAt: null,
            ReplaySuccess: null,
            ArchivedAt: null,
            UserNotes: "Investigating",
            CorrelationId: "corr-1",
            TopicName: null,
            ForensicRootCause: "Consumer timeout",
            ForensicConfidence: 0.85,
            ReplaySafety: "Safe");

        response.Id.Should().Be(1);
        response.MessageId.Should().Be("msg-1");
        response.FailureCategory.Should().Be("DeliveryFailure");
        response.ForensicRootCause.Should().Be("Consumer timeout");
    }

    [Fact]
    public void DlqMessageDetailResponse_ShouldCreateWithReplayHistory()
    {
        var replayHistory = new List<ReplayHistoryResponse>
        {
            new(1, DateTimeOffset.UtcNow, "user@test.com", "original-entity", "test-queue", "Success", null, null)
        };

        var response = new DlqMessageDetailResponse(
            Id: 1,
            MessageId: "msg-1",
            SequenceNumber: 100,
            BodyHash: "abc123",
            NamespaceId: Guid.NewGuid(),
            EntityName: "test-queue",
            EntityType: "Queue",
            EnqueuedTimeUtc: DateTimeOffset.UtcNow,
            DeadLetterTimeUtc: DateTimeOffset.UtcNow,
            DetectedAtUtc: DateTimeOffset.UtcNow,
            DeadLetterReason: "Test",
            DeadLetterErrorDescription: null,
            DeliveryCount: 5,
            ContentType: "application/json",
            MessageSize: 256,
            BodyPreview: "{}",
            ApplicationPropertiesJson: null,
            FailureCategory: "Transient",
            CategoryConfidence: 0.9,
            Status: "Replayed",
            ReplayedAt: DateTimeOffset.UtcNow,
            ReplaySuccess: true,
            ArchivedAt: null,
            UserNotes: null,
            CorrelationId: null,
            SessionId: null,
            TopicName: null,
            ReplayHistory: replayHistory);

        response.ReplayHistory.Should().HaveCount(1);
        response.ReplayHistory[0].OutcomeStatus.Should().Be("Success");
    }

    [Fact]
    public void DlqTimelineResponse_ShouldCreateWithEvents()
    {
        var events = new List<DlqTimelineEventResponse>
        {
            new("Enqueued", "Message enqueued", DateTimeOffset.UtcNow.AddHours(-2), null),
            new("DeadLettered", "Max delivery exceeded", DateTimeOffset.UtcNow.AddHours(-1),
                new Dictionary<string, string> { ["reason"] = "MaxDeliveryCountExceeded" })
        };

        var response = new DlqTimelineResponse(1, "test-queue", events);

        response.MessageId.Should().Be(1);
        response.Events.Should().HaveCount(2);
    }

    [Fact]
    public void DlqSummaryResponse_ShouldCreateWithStatistics()
    {
        var byCategory = new Dictionary<string, int> { ["Transient"] = 10, ["Poison"] = 3 };
        var byEntity = new Dictionary<string, int> { ["queue1"] = 8, ["queue2"] = 5 };
        var trend = new List<DlqTrendPointResponse>
        {
            new(DateTimeOffset.UtcNow.AddDays(-1), 5, 2),
            new(DateTimeOffset.UtcNow, 3, 4)
        };

        var response = new DlqSummaryResponse(
            TotalMessages: 100,
            ActiveMessages: 50,
            ReplayedMessages: 30,
            ArchivedMessages: 20,
            ByCategory: byCategory,
            ByEntity: byEntity,
            OldestMessage: DateTimeOffset.UtcNow.AddDays(-30),
            NewestMessage: DateTimeOffset.UtcNow,
            DailyTrend: trend);

        response.TotalMessages.Should().Be(100);
        response.ActiveMessages.Should().Be(50);
        response.ByCategory.Should().HaveCount(2);
        response.DailyTrend.Should().HaveCount(2);
    }

    [Fact]
    public void UpdateDlqNotesRequest_ShouldCreate()
    {
        var request = new UpdateDlqNotesRequest("Some notes about this message");
        request.Notes.Should().Be("Some notes about this message");
    }

    #endregion

    #region Rule Response DTOs

    [Fact]
    public void RuleResponse_ShouldCreateWithAllProperties()
    {
        var conditions = new List<RuleCondition>
        {
            new() { Field = "DeadLetterReason", Operator = "Contains", Value = "timeout" }
        };
        var action = new RuleAction { AutoReplay = true, DelaySeconds = 30 };

        var response = new RuleResponse(
            Id: 1,
            Name: "Timeout Rule",
            Description: "Auto-replay timeout errors",
            Enabled: true,
            Conditions: conditions,
            Action: action,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            MatchCount: 50,
            SuccessCount: 45,
            SuccessRate: 90.0,
            MaxReplaysPerHour: 100,
            PendingMatchCount: 0);

        response.Id.Should().Be(1);
        response.Name.Should().Be("Timeout Rule");
        response.Conditions.Should().HaveCount(1);
        response.SuccessRate.Should().Be(90.0);
    }

    [Fact]
    public void RuleTestResponse_ShouldCreate()
    {
        var matches = new List<RuleMatchResultResponse>
        {
            new(1, "msg-1", "queue1", true, "DeadLetterReason contains timeout", "Timeout"),
            new(2, "msg-2", "queue1", false, null, "InternalError")
        };

        var response = new RuleTestResponse(
            TotalTested: 10,
            MatchedCount: 1,
            EstimatedSuccessRate: 0.85,
            SampleMatches: matches);

        response.TotalTested.Should().Be(10);
        response.SampleMatches.Should().HaveCount(2);
    }

    [Fact]
    public void ReplayAllResponse_ShouldCreate()
    {
        var results = new List<ReplayAllItemResponse>
        {
            new(1, "msg-1", "queue1", "Success", null),
            new(2, "msg-2", "queue1", "Failed", "Connection timeout")
        };

        var response = new ReplayAllResponse(
            TotalMatched: 5,
            Replayed: 4,
            Failed: 1,
            Skipped: 0,
            Results: results);

        response.TotalMatched.Should().Be(5);
        response.Results.Should().HaveCount(2);
    }

    [Fact]
    public void RuleTemplateResponse_ShouldCreate()
    {
        var conditions = new List<RuleCondition>
        {
            new() { Field = "FailureCategory", Operator = "Equals", Value = "Transient" }
        };
        var action = new RuleAction { AutoReplay = true };

        var response = new RuleTemplateResponse(
            Id: "tmpl-1",
            Name: "Transient Template",
            Description: "Auto-replay transient errors",
            Category: "Built-in",
            Conditions: conditions,
            Action: action,
            UsageCount: 100,
            Rating: 4.5);

        response.Id.Should().Be("tmpl-1");
        response.Rating.Should().Be(4.5);
    }

    #endregion

    #region Request DTOs

    [Fact]
    public void CreateNamespaceRequest_ShouldCreate()
    {
        var request = new CreateNamespaceRequest(
            "test-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=value=",
            ConnectionAuthType.ConnectionString,
            "Test",
            "Description");

        request.Name.Should().Be("test-ns");
        request.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        request.DisplayName.Should().Be("Test");
    }

    [Fact]
    public void CreateRuleRequest_ShouldCreate()
    {
        var conditions = new List<RuleCondition>
        {
            new() { Field = "EntityName", Operator = "Equals", Value = "orders-queue" }
        };
        var action = new RuleAction { AutoReplay = true, MaxRetries = 5 };

        var request = new CreateRuleRequest
        {
            Name = "Orders Rule",
            Description = "Replay orders",
            Enabled = true,
            Conditions = conditions,
            Action = action,
            MaxReplaysPerHour = 200
        };

        request.Name.Should().Be("Orders Rule");
        request.Conditions.Should().HaveCount(1);
        request.MaxReplaysPerHour.Should().Be(200);
    }

    [Fact]
    public void GetMessagesRequest_ShouldCreate()
    {
        var request = new GetMessagesRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: "test-queue",
            SubscriptionName: null,
            FromDeadLetter: true,
            MaxMessages: 50,
            FromSequenceNumber: 100);

        request.EntityName.Should().Be("test-queue");
        request.FromDeadLetter.Should().BeTrue();
        request.MaxMessages.Should().Be(50);
        request.FromSequenceNumber.Should().Be(100);
    }

    [Fact]
    public void GetMessagesRequest_ShouldHaveConstants()
    {
        GetMessagesRequest.MaxAllowedMessages.Should().Be(100);
        GetMessagesRequest.MinAllowedMessages.Should().Be(1);
    }

    [Fact]
    public void SendMessageRequest_ShouldCreate()
    {
        var props = new Dictionary<string, object> { ["env"] = "test" };
        var request = new SendMessageRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: "test-queue",
            Body: "{\"data\":42}",
            ContentType: "application/json",
            CorrelationId: "corr-1",
            SessionId: null,
            PartitionKey: null,
            Subject: "Test",
            ReplyTo: null,
            ReplyToSessionId: null,
            To: null,
            TimeToLiveSeconds: 3600,
            ScheduledEnqueueTimeUtc: null,
            ApplicationProperties: props);

        request.Body.Should().Contain("data");
        request.ContentType.Should().Be("application/json");
        request.TimeToLiveSeconds.Should().Be(3600);
        request.ApplicationProperties.Should().ContainKey("env");
    }

    [Fact]
    public void SendMessageRequest_ShouldHaveDefaults()
    {
        var request = new SendMessageRequest();
        request.Body.Should().Be("");
        request.NamespaceId.Should().BeNull();
        request.EntityName.Should().BeNull();
    }

    #endregion
}
