using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class ReplaySafetyClassifierTests
{
    private static DlqMessage CreateMessage(int deliveryCount = 1)
    {
        return new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = deliveryCount
        };
    }

    [Fact]
    public void Classify_Transient_LowDelivery_ReturnsSafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(3), FailureCategory.Transient);
        result.Should().Be(ReplaySafetyClassifier.Safe);
    }

    [Fact]
    public void Classify_Transient_HighDelivery_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(15), FailureCategory.Transient);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Classify_Transient_ExactlyTenDeliveries_ReturnsSafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(10), FailureCategory.Transient);
        result.Should().Be(ReplaySafetyClassifier.Safe);
    }

    [Fact]
    public void Classify_Expired_ReturnsUnsafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.Expired);
        result.Should().Be(ReplaySafetyClassifier.Unsafe);
    }

    [Fact]
    public void Classify_Authorization_ReturnsUnsafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.Authorization);
        result.Should().Be(ReplaySafetyClassifier.Unsafe);
    }

    [Fact]
    public void Classify_DataQuality_ReturnsUnsafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.DataQuality);
        result.Should().Be(ReplaySafetyClassifier.Unsafe);
    }

    [Fact]
    public void Classify_QuotaExceeded_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.QuotaExceeded);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Classify_MaxDelivery_LowCount_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(3), FailureCategory.MaxDelivery);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Classify_MaxDelivery_HighCount_ReturnsUnsafe()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(10), FailureCategory.MaxDelivery);
        result.Should().Be(ReplaySafetyClassifier.Unsafe);
    }

    [Fact]
    public void Classify_ResourceNotFound_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.ResourceNotFound);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Classify_ProcessingError_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.ProcessingError);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Classify_Unknown_ReturnsRequiresReview()
    {
        var result = ReplaySafetyClassifier.Classify(CreateMessage(), FailureCategory.Unknown);
        result.Should().Be(ReplaySafetyClassifier.RequiresReview);
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        ReplaySafetyClassifier.Safe.Should().Be("Safe");
        ReplaySafetyClassifier.Unsafe.Should().Be("Unsafe");
        ReplaySafetyClassifier.RequiresReview.Should().Be("RequiresReview");
    }
}
