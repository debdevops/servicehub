using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class DeterministicClassifierTests
{
    private static DlqMessage CreateMessage(
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        string? bodyPreview = null,
        int deliveryCount = 1)
    {
        return new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            BodyPreview = bodyPreview,
            DeliveryCount = deliveryCount
        };
    }

    [Fact]
    public void Evaluate_MaxDeliveryCount_ReturnsMaxDelivery()
    {
        var msg = CreateMessage(deadLetterReason: "MaxDeliveryCountExceeded", deliveryCount: 10);

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.MaxDelivery);
        hit.Confidence.Should().Be(0.99);
    }

    [Fact]
    public void Evaluate_TTLExpired_ReturnsExpired()
    {
        var msg = CreateMessage(deadLetterReason: "TTLExpiredException");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Expired);
        hit.Confidence.Should().Be(0.99);
    }

    [Fact]
    public void Evaluate_HeaderSizeExceeded_ReturnsQuotaExceeded()
    {
        var msg = CreateMessage(deadLetterReason: "HeaderSizeExceeded");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.QuotaExceeded);
        hit.Confidence.Should().Be(0.98);
    }

    [Fact]
    public void Evaluate_MessageSizeExceeded_ReturnsQuotaExceeded()
    {
        var msg = CreateMessage(deadLetterReason: "MessageSizeExceeded");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.QuotaExceeded);
        hit.Confidence.Should().Be(0.98);
    }

    [Fact]
    public void Evaluate_SessionFilterMismatch_ReturnsProcessingError()
    {
        var msg = CreateMessage(deadLetterReason: "SessionFilterMismatch");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ProcessingError);
        hit.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void Evaluate_Http401_ReturnsAuthorization()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "HTTP 401 Unauthorized error");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Authorization);
        hit.Confidence.Should().Be(0.97);
    }

    [Fact]
    public void Evaluate_Http403_ReturnsAuthorization()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "HTTP 403 Forbidden");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Authorization);
        hit.Confidence.Should().Be(0.97);
    }

    [Fact]
    public void Evaluate_JsonException_ReturnsDataQuality()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "JsonException at line 5");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.DataQuality);
        hit.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void Evaluate_JsonDeserialization_ReturnsDataQuality()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "json deserialization failed");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.DataQuality);
        hit.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void Evaluate_ConnectionRefused_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Connection refused by target host");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
        hit.Confidence.Should().Be(0.93);
    }

    [Fact]
    public void Evaluate_SqlTimeout_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "SQL query timeout occurred");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
        hit.Confidence.Should().Be(0.92);
    }

    [Fact]
    public void Evaluate_SqlTimeoutCombined_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "SqlTimeout: execution timed out");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
    }

    [Fact]
    public void Evaluate_AppDeadLetterWithException_ReturnsProcessingError()
    {
        var msg = CreateMessage(
            deadLetterReason: "InvalidOrderState",
            deadLetterErrorDescription: "System.InvalidOperationException: Order not in correct state");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ProcessingError);
        hit.Confidence.Should().Be(0.90);
        hit.RootCause.Should().Contain("InvalidOrderState");
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsNull()
    {
        var msg = CreateMessage(deadLetterReason: null, deadLetterErrorDescription: null);

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().BeNull();
    }

    [Fact]
    public void Evaluate_EmptyReasonAndDescription_ReturnsNull()
    {
        var msg = CreateMessage(deadLetterReason: "", deadLetterErrorDescription: "");

        var hit = DeterministicClassifier.Evaluate(msg);

        hit.Should().BeNull();
    }
}
