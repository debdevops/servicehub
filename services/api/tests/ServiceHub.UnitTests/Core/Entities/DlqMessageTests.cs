using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Core.Entities;

public sealed class DlqMessageTests
{
    private static DlqMessage MakeMessage(
        string? reason = null,
        string? desc = null,
        int deliveryCount = 1,
        string? appPropsJson = null)
    {
        return new DlqMessage
        {
            MessageId = "msg-001",
            SequenceNumber = 1,
            BodyHash = "deadbeef",
            NamespaceId = Guid.NewGuid(),
            EntityName = "orders-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = reason,
            DeadLetterErrorDescription = desc,
            DeliveryCount = deliveryCount,
            ApplicationPropertiesJson = appPropsJson,
        };
    }

    [Fact]
    public void Defaults_FailureCategory_IsUnknown()
    {
        var msg = MakeMessage();
        msg.FailureCategory.Should().Be(FailureCategory.Unknown);
    }

    [Fact]
    public void Defaults_Status_IsActive()
    {
        var msg = MakeMessage();
        msg.Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public void Defaults_CategoryConfidence_IsZero()
    {
        var msg = MakeMessage();
        msg.CategoryConfidence.Should().Be(0.0);
    }

    [Fact]
    public void Defaults_ForensicConfidence_IsZero()
    {
        var msg = MakeMessage();
        msg.ForensicConfidence.Should().Be(0.0);
    }

    [Fact]
    public void Defaults_ReplaySafety_IsNull()
    {
        var msg = MakeMessage();
        msg.ReplaySafety.Should().BeNull();
    }

    [Fact]
    public void Defaults_ForensicRootCause_IsNull()
    {
        var msg = MakeMessage();
        msg.ForensicRootCause.Should().BeNull();
    }

    [Fact]
    public void Defaults_Timestamps_AreNullable()
    {
        var msg = MakeMessage();
        msg.DeadLetterTimeUtc.Should().BeNull();
        msg.ReplayedAt.Should().BeNull();
        msg.ArchivedAt.Should().BeNull();
        msg.ResolvedAt.Should().BeNull();
        msg.ReplaySuccess.Should().BeNull();
    }

    [Fact]
    public void Status_CanBeMutated()
    {
        var msg = MakeMessage();
        msg.Status = DlqMessageStatus.Replayed;
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
    }

    [Fact]
    public void ForensicFields_CanBeMutated()
    {
        var msg = MakeMessage();
        msg.FailureCategory = FailureCategory.Transient;
        msg.ForensicConfidence = 0.95;
        msg.ForensicRootCause = "Downstream timeout";
        msg.ReplaySafety = "Safe";

        msg.FailureCategory.Should().Be(FailureCategory.Transient);
        msg.ForensicConfidence.Should().Be(0.95);
        msg.ForensicRootCause.Should().Be("Downstream timeout");
        msg.ReplaySafety.Should().Be("Safe");
    }

    [Fact]
    public void RequiredProperties_AreStoredCorrectly()
    {
        var namespaceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var msg = new DlqMessage
        {
            MessageId = "test-id",
            SequenceNumber = 42,
            BodyHash = "abc123",
            NamespaceId = namespaceId,
            EntityName = "my-queue",
            EntityType = ServiceBusEntityType.Subscription,
            EnqueuedTimeUtc = now,
            DetectedAtUtc = now,
        };

        msg.MessageId.Should().Be("test-id");
        msg.SequenceNumber.Should().Be(42);
        msg.BodyHash.Should().Be("abc123");
        msg.NamespaceId.Should().Be(namespaceId);
        msg.EntityName.Should().Be("my-queue");
        msg.EntityType.Should().Be(ServiceBusEntityType.Subscription);
    }

    [Fact]
    public void UserNotes_CanBeSet()
    {
        var msg = MakeMessage();
        msg.UserNotes = "Investigate this message";
        msg.UserNotes.Should().Be("Investigate this message");
    }
}
