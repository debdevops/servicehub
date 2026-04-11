using FluentAssertions;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.DTOs;

/// <summary>
/// Tests that exercise record equality, With-expressions, deconstruction and
/// all properties of DTO records.  These ensure full code coverage of the
/// compiler-generated members (Equals, GetHashCode, ToString, PrintMembers,
/// Deconstruct, property getters, With-copy-ctor).
/// </summary>
public class DtoRecordEqualityTests
{
    // ---------------------------------------------------------------
    // DlqMessageDetailResponse — covers the 30+ properties
    // ---------------------------------------------------------------

    [Fact]
    public void DlqMessageDetailResponse_RecordEquality()
    {
        var nsId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var replayHistory = new List<ReplayHistoryResponse>
        {
            new(1, now, "admin", "original-entity", "queue1", "Success", null, null)
        };

        var a = new DlqMessageDetailResponse(
            Id: 42, MessageId: "m1", SequenceNumber: 99, BodyHash: "h",
            NamespaceId: nsId, EntityName: "q1", EntityType: "Queue",
            EnqueuedTimeUtc: now, DeadLetterTimeUtc: now, DetectedAtUtc: now,
            DeadLetterReason: "MaxDelivery", DeadLetterErrorDescription: "desc",
            DeliveryCount: 10, ContentType: "application/json", MessageSize: 512,
            BodyPreview: "{}", ApplicationPropertiesJson: "{\"a\":1}",
            FailureCategory: "Transient", CategoryConfidence: 0.9,
            Status: "Active", ReplayedAt: null, ReplaySuccess: null,
            ArchivedAt: null, UserNotes: "note", CorrelationId: "c1",
            SessionId: "s1", TopicName: null, ReplayHistory: replayHistory);

        var b = a with { Id = 42 }; // identical copy

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.ToString().Should().Contain("DlqMessageDetailResponse");

        // Verify every property accessor
        a.Id.Should().Be(42);
        a.MessageId.Should().Be("m1");
        a.SequenceNumber.Should().Be(99);
        a.BodyHash.Should().Be("h");
        a.NamespaceId.Should().Be(nsId);
        a.EntityName.Should().Be("q1");
        a.EntityType.Should().Be("Queue");
        a.DeadLetterReason.Should().Be("MaxDelivery");
        a.DeadLetterErrorDescription.Should().Be("desc");
        a.DeliveryCount.Should().Be(10);
        a.ContentType.Should().Be("application/json");
        a.MessageSize.Should().Be(512);
        a.BodyPreview.Should().Be("{}");
        a.ApplicationPropertiesJson.Should().Be("{\"a\":1}");
        a.FailureCategory.Should().Be("Transient");
        a.CategoryConfidence.Should().Be(0.9);
        a.Status.Should().Be("Active");
        a.ReplayedAt.Should().BeNull();
        a.ReplaySuccess.Should().BeNull();
        a.ArchivedAt.Should().BeNull();
        a.UserNotes.Should().Be("note");
        a.CorrelationId.Should().Be("c1");
        a.SessionId.Should().Be("s1");
        a.TopicName.Should().BeNull();
        a.ReplayHistory.Should().HaveCount(1);
    }

    [Fact]
    public void DlqMessageDetailResponse_NotEqual_WhenDifferent()
    {
        var now = DateTimeOffset.UtcNow;
        var a = CreateDetailResponse(now, "m1");
        var b = CreateDetailResponse(now, "m2");
        a.Should().NotBe(b);
    }

    // ---------------------------------------------------------------
    // ReplayHistoryResponse
    // ---------------------------------------------------------------

    [Fact]
    public void ReplayHistoryResponse_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new ReplayHistoryResponse(
            Id: 7, ReplayedAt: now, ReplayedBy: "user@x.com",
            ReplayStrategy: "original-entity", ReplayedToEntity: "queue1",
            OutcomeStatus: "Failed", NewDeadLetterReason: "Timeout",
            ErrorDetails: "Connection refused");

        r.Id.Should().Be(7);
        r.ReplayedBy.Should().Be("user@x.com");
        r.OutcomeStatus.Should().Be("Failed");
        r.NewDeadLetterReason.Should().Be("Timeout");
        r.ErrorDetails.Should().Be("Connection refused");

        var copy = r with { OutcomeStatus = "Success" };
        copy.Should().NotBe(r);
    }

    // ---------------------------------------------------------------
    // DlqTimelineEventResponse
    // ---------------------------------------------------------------

    [Fact]
    public void DlqTimelineEventResponse_AllProperties()
    {
        var details = new Dictionary<string, string> { ["key"] = "val" };
        var e = new DlqTimelineEventResponse("Enqueued", "Message enqueued",
            DateTimeOffset.UtcNow, details);

        e.EventType.Should().Be("Enqueued");
        e.Description.Should().Be("Message enqueued");
        e.Details.Should().ContainKey("key");
    }

    // ---------------------------------------------------------------
    // TopicRuntimePropertiesDto — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void TopicRuntimePropertiesDto_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var dto = new TopicRuntimePropertiesDto(
            Name: "t1", SubscriptionCount: 3, SizeInBytes: 8192,
            Status: "Active", CreatedAt: now, UpdatedAt: now, AccessedAt: now,
            RequiresDuplicateDetection: true, EnablePartitioning: false,
            EnableBatchedOperations: true, SupportOrdering: true,
            MaxSizeInMegabytes: 4096,
            DefaultMessageTimeToLive: TimeSpan.FromDays(7),
            AutoDeleteOnIdle: TimeSpan.FromDays(365),
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));

        dto.Name.Should().Be("t1");
        dto.SubscriptionCount.Should().Be(3);
        dto.SizeInBytes.Should().Be(8192);
        dto.RequiresDuplicateDetection.Should().BeTrue();
        dto.SupportOrdering.Should().BeTrue();
        dto.MaxSizeInMegabytes.Should().Be(4096);
        dto.AutoDeleteOnIdle.Should().Be(TimeSpan.FromDays(365));
        dto.DuplicateDetectionHistoryTimeWindow.Should().Be(TimeSpan.FromMinutes(10));

        var copy = dto with { Name = "t2" };
        copy.Should().NotBe(dto);
    }

    // ---------------------------------------------------------------
    // QueueRuntimePropertiesDto — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void QueueRuntimePropertiesDto_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var dto = new QueueRuntimePropertiesDto(
            Name: "q1", ActiveMessageCount: 100, DeadLetterMessageCount: 5,
            ScheduledMessageCount: 2, TransferMessageCount: 1,
            TransferDeadLetterMessageCount: 0, SizeInBytes: 4096,
            Status: "Active", CreatedAt: now, UpdatedAt: now, AccessedAt: now,
            RequiresSession: true, RequiresDuplicateDetection: false,
            EnablePartitioning: true, EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024, MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue);

        dto.ScheduledMessageCount.Should().Be(2);
        dto.TransferMessageCount.Should().Be(1);
        dto.TransferDeadLetterMessageCount.Should().Be(0);
        dto.SizeInBytes.Should().Be(4096);
        dto.Status.Should().Be("Active");
        dto.RequiresSession.Should().BeTrue();
        dto.EnablePartitioning.Should().BeTrue();
        dto.EnableBatchedOperations.Should().BeTrue();
        dto.MaxSizeInMegabytes.Should().Be(1024);
        dto.DefaultMessageTimeToLive.Should().Be(TimeSpan.FromDays(14));
        dto.LockDuration.Should().Be(TimeSpan.FromSeconds(30));
    }

    // ---------------------------------------------------------------
    // SubscriptionRuntimePropertiesDto — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void SubscriptionRuntimePropertiesDto_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var dto = new SubscriptionRuntimePropertiesDto(
            Name: "sub1", TopicName: "topic1", ActiveMessageCount: 25,
            DeadLetterMessageCount: 3, TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0, Status: "Active",
            CreatedAt: now, UpdatedAt: now, AccessedAt: now,
            RequiresSession: false, EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: true,
            EnableDeadLetteringOnFilterEvaluationExceptions: true,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(60),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: null, ForwardDeadLetteredMessagesTo: "dlq-forward");

        dto.TransferMessageCount.Should().Be(0);
        dto.TransferDeadLetterMessageCount.Should().Be(0);
        dto.EnableDeadLetteringOnFilterEvaluationExceptions.Should().BeTrue();
        dto.LockDuration.Should().Be(TimeSpan.FromSeconds(60));
        dto.ForwardDeadLetteredMessagesTo.Should().Be("dlq-forward");

        var copy = dto with { Name = "sub2" };
        copy.TopicName.Should().Be("topic1");
    }

    // ---------------------------------------------------------------
    // MessageResponse — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void MessageResponse_AllProperties_Accessors()
    {
        var props = new Dictionary<string, object> { ["env"] = "prod" };
        var now = DateTimeOffset.UtcNow;
        var msg = new MessageResponse(
            MessageId: "m1", SequenceNumber: 42, Body: "body",
            ContentType: "text/plain", CorrelationId: "c1", SessionId: "s1",
            PartitionKey: "pk", Subject: "subj", ReplyTo: "rq",
            ReplyToSessionId: "rs", To: "dest",
            TimeToLive: TimeSpan.FromHours(1),
            ScheduledEnqueueTime: now.AddMinutes(5),
            EnqueuedTime: now, ExpiresAt: now.AddHours(1),
            LockedUntil: now.AddSeconds(30),
            DeliveryCount: 1, State: MessageState.Active,
            DeadLetterSource: "src", DeadLetterReason: "reason",
            DeadLetterErrorDescription: "err",
            ApplicationProperties: props, SizeInBytes: 128,
            EntityName: "eq", SubscriptionName: "sub1",
            IsFromDeadLetter: true);

        msg.SessionId.Should().Be("s1");
        msg.PartitionKey.Should().Be("pk");
        msg.Subject.Should().Be("subj");
        msg.ReplyTo.Should().Be("rq");
        msg.ReplyToSessionId.Should().Be("rs");
        msg.To.Should().Be("dest");
        msg.TimeToLive.Should().Be(TimeSpan.FromHours(1));
        msg.ScheduledEnqueueTime.Should().NotBeNull();
        msg.ExpiresAt.Should().NotBeNull();
        msg.LockedUntil.Should().NotBeNull();
        msg.DeadLetterSource.Should().Be("src");
        msg.DeadLetterReason.Should().Be("reason");
        msg.DeadLetterErrorDescription.Should().Be("err");
        msg.EntityName.Should().Be("eq");
        msg.SubscriptionName.Should().Be("sub1");
        msg.IsFromDeadLetter.Should().BeTrue();
        msg.ApplicationProperties.Should().ContainKey("env");
    }

    // ---------------------------------------------------------------
    // CorrelationTimelineEntry — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void CorrelationTimelineEntry_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var nsId = Guid.NewGuid();
        var entry = new CorrelationTimelineEntry(
            Source: "Live", NamespaceId: nsId,
            NamespaceDisplayName: "My NS", EntityName: "q1",
            EntityPath: "topic1/subscriptions/sub1",
            MessageId: "m1", SequenceNumber: 100,
            State: "Active", Timestamp: now,
            DeadLetterReason: null, BodyPreview: "preview",
            SizeInBytes: 256);

        entry.Source.Should().Be("Live");
        entry.NamespaceId.Should().Be(nsId);
        entry.NamespaceDisplayName.Should().Be("My NS");
        entry.EntityPath.Should().Be("topic1/subscriptions/sub1");
        entry.BodyPreview.Should().Be("preview");
        entry.SizeInBytes.Should().Be(256);

        var histEntry = entry with { Source = "History", State = "DeadLettered" };
        histEntry.Should().NotBe(entry);
    }

    [Fact]
    public void CorrelationTimelineResponse_AllProperties()
    {
        var entries = new List<CorrelationTimelineEntry>();
        var response = new CorrelationTimelineResponse(
            CorrelationId: "c1", Entries: entries, TotalCount: 0,
            NamespacesSearched: 3, EntitiesSearched: 12,
            IsPartialResult: false, SearchDurationMs: 150);

        response.CorrelationId.Should().Be("c1");
        response.NamespacesSearched.Should().Be(3);
        response.EntitiesSearched.Should().Be(12);
        response.IsPartialResult.Should().BeFalse();
        response.SearchDurationMs.Should().Be(150);
    }

    // ---------------------------------------------------------------
    // RuleMatchResultResponse — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void RuleMatchResultResponse_AllProperties()
    {
        var r = new RuleMatchResultResponse(
            MessageId: 1, ServiceBusMessageId: "sb-1",
            EntityName: "q1", IsMatch: true,
            MatchReason: "reason contains timeout",
            DeadLetterReason: "MaxDelivery");

        r.MessageId.Should().Be(1);
        r.ServiceBusMessageId.Should().Be("sb-1");
        r.IsMatch.Should().BeTrue();
        r.MatchReason.Should().Contain("timeout");
    }

    // ---------------------------------------------------------------
    // ReplayAllItemResponse — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void ReplayAllItemResponse_AllProperties()
    {
        var item = new ReplayAllItemResponse(
            DlqRecordId: 10, MessageId: "m1", EntityName: "q1",
            Outcome: "Success", Error: null);

        item.DlqRecordId.Should().Be(10);
        item.MessageId.Should().Be("m1");
        item.Outcome.Should().Be("Success");
        item.Error.Should().BeNull();

        var failed = item with { Outcome = "Failed", Error = "Timeout" };
        failed.Error.Should().Be("Timeout");
    }

    // ---------------------------------------------------------------
    // RuleTemplateResponse — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void RuleTemplateResponse_AllProperties()
    {
        var t = new RuleTemplateResponse(
            Id: "t1", Name: "Template", Description: "desc",
            Category: "Built-in",
            Conditions: new List<RuleCondition>(),
            Action: new RuleAction(),
            UsageCount: 50, Rating: 4.8);

        t.Category.Should().Be("Built-in");
        t.UsageCount.Should().Be(50);
        t.Description.Should().Be("desc");
    }

    // ---------------------------------------------------------------
    // GenerateRulesResponse
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateRulesResponse_AllProperties()
    {
        var r = new GenerateRulesResponse(
            AnalysedMessages: 100, PatternsDetected: 5,
            RulesCreated: 3, RulesSkipped: 2,
            Rules: new List<RuleResponse>());

        r.AnalysedMessages.Should().Be(100);
        r.PatternsDetected.Should().Be(5);
        r.RulesCreated.Should().Be(3);
        r.RulesSkipped.Should().Be(2);
        r.Rules.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // DlqSummaryResponse — all properties
    // ---------------------------------------------------------------

    [Fact]
    public void DlqSummaryResponse_AllProperties()
    {
        var s = new DlqSummaryResponse(
            TotalMessages: 200, ActiveMessages: 100,
            ReplayedMessages: 80, ArchivedMessages: 20,
            ByCategory: new Dictionary<string, int> { ["T"] = 50 },
            ByEntity: new Dictionary<string, int> { ["q1"] = 100 },
            OldestMessage: null, NewestMessage: null,
            DailyTrend: new List<DlqTrendPointResponse>());

        s.ReplayedMessages.Should().Be(80);
        s.ArchivedMessages.Should().Be(20);
        s.OldestMessage.Should().BeNull();
        s.NewestMessage.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // DlqTrendPointResponse
    // ---------------------------------------------------------------

    [Fact]
    public void DlqTrendPointResponse_AllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new DlqTrendPointResponse(now, 10, 5);
        p.Date.Should().Be(now);
        p.NewMessages.Should().Be(10);
        p.ResolvedMessages.Should().Be(5);
    }

    // ---------------------------------------------------------------
    // DlqHistoryResponse — with-expression & inequality
    // ---------------------------------------------------------------

    [Fact]
    public void DlqHistoryResponse_WithExpression()
    {
        var now = DateTimeOffset.UtcNow;
        var nsId = Guid.NewGuid();
        var r = new DlqHistoryResponse(
            Id: 1, MessageId: "m1", SequenceNumber: 1, BodyHash: "h",
            NamespaceId: nsId, EntityName: "q1", EntityType: "Queue",
            EnqueuedTimeUtc: now, DeadLetterTimeUtc: now, DetectedAtUtc: now,
            DeadLetterReason: "R", DeadLetterErrorDescription: "D",
            DeliveryCount: 5, ContentType: "json", MessageSize: 100,
            BodyPreview: "{}", FailureCategory: "Transient",
            CategoryConfidence: 0.8, Status: "Active",
            ReplayedAt: null, ReplaySuccess: null, ArchivedAt: null,
            UserNotes: null, CorrelationId: null, TopicName: null,
            ForensicRootCause: null, ForensicConfidence: 0.0,
            ReplaySafety: null);

        var modified = r with { Status = "Replayed", ReplayedAt = now };
        modified.Status.Should().Be("Replayed");
        modified.ReplayedAt.Should().Be(now);
        modified.Should().NotBe(r);

        // Access all nullable properties
        r.TopicName.Should().BeNull();
        r.CorrelationId.Should().BeNull();
        r.UserNotes.Should().BeNull();
        r.ForensicRootCause.Should().BeNull();
        r.ReplaySafety.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // RuleResponse — with-expression
    // ---------------------------------------------------------------

    [Fact]
    public void RuleResponse_WithExpression()
    {
        var r = new RuleResponse(
            Id: 1, Name: "R1", Description: null, Enabled: true,
            Conditions: new List<RuleCondition>(),
            Action: new RuleAction(), CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null, MatchCount: 0, SuccessCount: 0,
            SuccessRate: 0, MaxReplaysPerHour: 100, PendingMatchCount: 0);

        var updated = r with { Enabled = false, MatchCount = 10 };
        updated.Enabled.Should().BeFalse();
        updated.MatchCount.Should().Be(10);
        updated.Description.Should().BeNull();
        updated.UpdatedAt.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // RuleTestResponse — with-expression
    // ---------------------------------------------------------------

    [Fact]
    public void RuleTestResponse_AllProperties()
    {
        var t = new RuleTestResponse(
            TotalTested: 50, MatchedCount: 10,
            EstimatedSuccessRate: 0.75,
            SampleMatches: new List<RuleMatchResultResponse>());

        t.TotalTested.Should().Be(50);
        t.EstimatedSuccessRate.Should().Be(0.75);
        t.SampleMatches.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // ReplayAllResponse — with-expression
    // ---------------------------------------------------------------

    [Fact]
    public void ReplayAllResponse_WithExpression()
    {
        var r = new ReplayAllResponse(
            TotalMatched: 20, Replayed: 15, Failed: 3, Skipped: 2,
            Results: new List<ReplayAllItemResponse>());

        var updated = r with { Failed = 0 };
        updated.Failed.Should().Be(0);
        updated.Skipped.Should().Be(2);
    }

    // ---------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------

    private static DlqMessageDetailResponse CreateDetailResponse(DateTimeOffset now, string messageId)
    {
        return new DlqMessageDetailResponse(
            Id: 1, MessageId: messageId, SequenceNumber: 1, BodyHash: "h",
            NamespaceId: Guid.NewGuid(), EntityName: "q1", EntityType: "Queue",
            EnqueuedTimeUtc: now, DeadLetterTimeUtc: now, DetectedAtUtc: now,
            DeadLetterReason: null, DeadLetterErrorDescription: null,
            DeliveryCount: 1, ContentType: null, MessageSize: 100,
            BodyPreview: null, ApplicationPropertiesJson: null,
            FailureCategory: "Unknown", CategoryConfidence: 0,
            Status: "Active", ReplayedAt: null, ReplaySuccess: null,
            ArchivedAt: null, UserNotes: null, CorrelationId: null,
            SessionId: null, TopicName: null,
            ReplayHistory: new List<ReplayHistoryResponse>());
    }
}
