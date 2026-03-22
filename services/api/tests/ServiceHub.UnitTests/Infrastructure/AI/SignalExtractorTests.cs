using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class SignalExtractorTests
{
    [Fact]
    public void CombinedText_AllFieldsPopulated_JoinsAll()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Message could not be processed",
            BodyPreview = "{ \"orderId\": 123 }"
        };

        var result = SignalExtractor.CombinedText(msg);

        result.Should().Contain("maxdeliverycountexceeded");
        result.Should().Contain("message could not be processed");
        result.Should().Contain("orderid");
    }

    [Fact]
    public void CombinedText_NullFields_HandlesGracefully()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = null,
            DeadLetterErrorDescription = null,
            BodyPreview = null
        };

        var result = SignalExtractor.CombinedText(msg);

        result.Should().NotBeNull();
    }

    [Fact]
    public void CombinedText_OnlyReason_ContainsReason()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = "TTLExpiredException"
        };

        var result = SignalExtractor.CombinedText(msg);

        result.Should().Contain("ttlexpiredexception");
    }

    [Fact]
    public void CombinedText_OnlyDescription_ContainsDescription()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterErrorDescription = "Authorization failed"
        };

        var result = SignalExtractor.CombinedText(msg);

        result.Should().Contain("authorization failed");
    }

    [Fact]
    public void CombinedText_OnlyBodyPreview_ContainsBody()
    {
        var msg = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            BodyPreview = "some body content"
        };

        var result = SignalExtractor.CombinedText(msg);

        result.Should().Contain("some body content");
    }
}
