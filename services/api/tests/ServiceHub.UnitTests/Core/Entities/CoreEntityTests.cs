using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.Entities;

public class CoreEntityTests
{
    #region Message Entity Tests

    [Fact]
    public void Message_ShouldCreateWithRequiredProperties()
    {
        var msg = new Message
        {
            MessageId = "msg-001",
            SequenceNumber = 42,
            Body = "{\"key\":\"value\"}",
            ContentType = "application/json",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 1,
            State = MessageState.Active,
            SizeInBytes = 256,
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            IsFromDeadLetter = false
        };

        msg.MessageId.Should().Be("msg-001");
        msg.SequenceNumber.Should().Be(42);
        msg.Body.Should().Contain("key");
        msg.ContentType.Should().Be("application/json");
        msg.State.Should().Be(MessageState.Active);
        msg.IsFromDeadLetter.Should().BeFalse();
    }

    [Fact]
    public void Message_ShouldSupportDeadLetterProperties()
    {
        var msg = new Message
        {
            MessageId = "dlq-001",
            SequenceNumber = 100,
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 10,
            State = MessageState.DeadLettered,
            DeadLetterSource = "test-queue",
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Exceeded max delivery count of 10",
            SizeInBytes = 512,
            IsFromDeadLetter = true
        };

        msg.State.Should().Be(MessageState.DeadLettered);
        msg.DeadLetterSource.Should().Be("test-queue");
        msg.DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");
        msg.IsFromDeadLetter.Should().BeTrue();
    }

    [Fact]
    public void Message_ShouldSupportAllOptionalProperties()
    {
        var props = new Dictionary<string, object> { ["env"] = "prod", ["version"] = 2 };
        var msg = new Message
        {
            MessageId = "msg-full",
            SequenceNumber = 1,
            Body = "test body",
            ContentType = "text/plain",
            CorrelationId = "corr-1",
            SessionId = "sess-1",
            PartitionKey = "pk-1",
            ReplyTo = "reply-queue",
            ReplyToSessionId = "reply-sess",
            To = "destination",
            Subject = "Test Subject",
            TimeToLive = TimeSpan.FromHours(1),
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddMinutes(5),
            EnqueuedTime = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            LockedUntil = DateTimeOffset.UtcNow.AddSeconds(30),
            LockToken = "lock-token-123",
            DeliveryCount = 1,
            State = MessageState.Active,
            ApplicationProperties = props,
            SizeInBytes = 128,
            NamespaceId = Guid.NewGuid(),
            EntityName = "my-queue",
            SubscriptionName = "my-sub",
            IsFromDeadLetter = false,
            EnqueuedSequenceNumber = 50
        };

        msg.CorrelationId.Should().Be("corr-1");
        msg.SessionId.Should().Be("sess-1");
        msg.PartitionKey.Should().Be("pk-1");
        msg.Subject.Should().Be("Test Subject");
        msg.ApplicationProperties.Should().ContainKey("env");
        msg.SubscriptionName.Should().Be("my-sub");
        msg.EnqueuedSequenceNumber.Should().Be(50);
    }

    #endregion

    #region ReplayHistory Entity Tests

    [Fact]
    public void ReplayHistory_ShouldCreateWithRequiredProperties()
    {
        var history = new ReplayHistory
        {
            DlqMessageId = 42,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "user@test.com",
            ReplayStrategy = "original-entity",
            ReplayedToEntity = "test-queue",
            OutcomeStatus = "Success"
        };

        history.DlqMessageId.Should().Be(42);
        history.ReplayedBy.Should().Be("user@test.com");
        history.ReplayStrategy.Should().Be("original-entity");
        history.OutcomeStatus.Should().Be("Success");
        history.RuleId.Should().BeNull();
    }

    [Fact]
    public void ReplayHistory_ShouldSupportOptionalProperties()
    {
        var history = new ReplayHistory
        {
            DlqMessageId = 43,
            RuleId = 5,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "auto-rule",
            ReplayStrategy = "alternate-entity",
            ReplayedToEntity = "retry-queue",
            OutcomeStatus = "Failed",
            NewDeadLetterReason = "Still failing",
            ErrorDetails = "Connection timeout on retry"
        };

        history.RuleId.Should().Be(5);
        history.ReplayStrategy.Should().Be("alternate-entity");
        history.OutcomeStatus.Should().Be("Failed");
        history.NewDeadLetterReason.Should().Be("Still failing");
        history.ErrorDetails.Should().Contain("timeout");
    }

    #endregion

    #region MessageState Enum Tests

    [Fact]
    public void MessageState_ShouldHaveExpectedValues()
    {
        ((int)MessageState.Active).Should().Be(0);
        ((int)MessageState.Deferred).Should().Be(1);
        ((int)MessageState.Scheduled).Should().Be(2);
        ((int)MessageState.DeadLettered).Should().Be(3);
        ((int)MessageState.Completed).Should().Be(4);
        ((int)MessageState.Abandoned).Should().Be(5);
    }

    #endregion

    #region ConnectionAuthType Enum Tests

    [Fact]
    public void ConnectionAuthType_ShouldHaveExpectedValues()
    {
        ((int)ConnectionAuthType.ConnectionString).Should().Be(0);
        ((int)ConnectionAuthType.ManagedIdentity).Should().Be(1);
        ((int)ConnectionAuthType.ServicePrincipal).Should().Be(2);
        ((int)ConnectionAuthType.DefaultAzureCredential).Should().Be(3);
    }

    #endregion

    #region RuleCondition Model Tests

    [Fact]
    public void RuleCondition_ShouldCreateWithRequiredProperties()
    {
        var condition = new RuleCondition
        {
            Field = "DeadLetterReason",
            Operator = "Contains",
            Value = "timeout"
        };

        condition.Field.Should().Be("DeadLetterReason");
        condition.Operator.Should().Be("Contains");
        condition.Value.Should().Be("timeout");
        condition.CaseSensitive.Should().BeFalse();
        condition.PropertyKey.Should().BeNull();
    }

    [Fact]
    public void RuleCondition_ShouldSupportApplicationProperty()
    {
        var condition = new RuleCondition
        {
            Field = "ApplicationProperty",
            Operator = "Equals",
            Value = "production",
            CaseSensitive = true,
            PropertyKey = "environment"
        };

        condition.Field.Should().Be("ApplicationProperty");
        condition.CaseSensitive.Should().BeTrue();
        condition.PropertyKey.Should().Be("environment");
    }

    #endregion

    #region RuleAction Model Tests

    [Fact]
    public void RuleAction_ShouldHaveDefaults()
    {
        var action = new RuleAction();

        action.AutoReplay.Should().BeTrue();
        action.DelaySeconds.Should().Be(60);
        action.MaxRetries.Should().Be(3);
        action.ExponentialBackoff.Should().BeFalse();
        action.TargetEntity.Should().BeNull();
    }

    [Fact]
    public void RuleAction_ShouldSetCustomValues()
    {
        var action = new RuleAction
        {
            AutoReplay = false,
            DelaySeconds = 120,
            MaxRetries = 5,
            ExponentialBackoff = true,
            TargetEntity = "retry-queue"
        };

        action.AutoReplay.Should().BeFalse();
        action.DelaySeconds.Should().Be(120);
        action.MaxRetries.Should().Be(5);
        action.ExponentialBackoff.Should().BeTrue();
        action.TargetEntity.Should().Be("retry-queue");
    }

    #endregion

    #region Namespace Entity Tests

    [Fact]
    public void Namespace_Create_ShouldReturnSuccess()
    {
        var result = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            "A test namespace");

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Contain("test-namespace");
        result.Value.DisplayName.Should().Be("Test NS");
        result.Value.Description.Should().Be("A test namespace");
        result.Value.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Namespace_CreateWithManagedIdentity_ShouldReturnSuccess()
    {
        var result = Namespace.CreateWithManagedIdentity(
            "test-mi-ns",
            ConnectionAuthType.ManagedIdentity,
            "MI Test",
            "Managed identity namespace");

        result.IsSuccess.Should().BeTrue();
        result.Value.AuthType.Should().Be(ConnectionAuthType.ManagedIdentity);
        result.Value.ConnectionString.Should().BeNull();
        result.Value.HasListenPermission.Should().BeTrue();
        result.Value.HasSendPermission.Should().BeTrue();
        result.Value.HasManagePermission.Should().BeTrue();
    }

    [Fact]
    public void Namespace_CreateWithManagedIdentity_ConnectionStringAuthType_ShouldFail()
    {
        var result = Namespace.CreateWithManagedIdentity(
            "test-ns",
            ConnectionAuthType.ConnectionString);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Namespace_UpdateDisplayName_ShouldSucceed()
    {
        var ns = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=").Value;

        var result = ns.UpdateDisplayName("New Display Name");

        result.IsSuccess.Should().BeTrue();
        ns.DisplayName.Should().Be("New Display Name");
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Namespace_UpdateDisplayName_TooLong_ShouldFail()
    {
        var ns = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=").Value;

        var result = ns.UpdateDisplayName(new string('a', 200));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Namespace_UpdateDescription_ShouldSucceed()
    {
        var ns = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=").Value;

        var result = ns.UpdateDescription("New description");

        result.IsSuccess.Should().BeTrue();
        ns.Description.Should().Be("New description");
    }

    [Fact]
    public void Namespace_UpdateDescription_TooLong_ShouldFail()
    {
        var ns = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=").Value;

        var result = ns.UpdateDescription(new string('a', 600));

        result.IsFailure.Should().BeTrue();
    }

    #endregion
}
