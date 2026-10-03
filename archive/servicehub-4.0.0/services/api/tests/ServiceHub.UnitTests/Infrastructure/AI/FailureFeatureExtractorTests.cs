using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class FailureFeatureExtractorTests
{
    private static readonly Guid NamespaceId = Guid.NewGuid();
    private const string OwnerId = "entra:test-owner";

    private readonly FailureFeatureExtractor _sut = new();

    private static DlqMessage CreateMessage(
        long id,
        string deadLetterReason = "MaxDeliveryCountExceeded",
        string entityName = "test-queue",
        int deliveryCount = 1,
        string? deadLetterErrorDescription = null,
        string? applicationPropertiesJson = null,
        string? bodyPreview = null)
    {
        var detectedAt = DateTimeOffset.UtcNow;
        var message = new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = $"hash-{id}",
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = detectedAt.AddMinutes(-5),
            DeadLetterTimeUtc = detectedAt,
            DetectedAtUtc = detectedAt,
            DeadLetterReason = deadLetterReason,
            DeliveryCount = deliveryCount,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            ApplicationPropertiesJson = applicationPropertiesJson,
            BodyPreview = bodyPreview,
            CloudProvider = CloudProviderType.Azure,
        };

        typeof(DlqMessage).GetProperty(nameof(DlqMessage.Id))!.SetValue(message, id);
        return message;
    }

    [Fact]
    public async Task ExtractAsync_WithValidMessage_ReturnsFeatures()
    {
        var message = CreateMessage(1);

        var result = await _sut.ExtractAsync(message);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");
        result.Value.EntityName.Should().Be("test-queue");
        result.Value.Provider.Should().Be(CloudProviderType.Azure);
        result.Value.DeliveryCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtractAsync_WithTimeoutInBodyPreview_DetectsTimeoutException()
    {
        var message = CreateMessage(1, bodyPreview: "Request timeout after 30 seconds");

        var result = await _sut.ExtractAsync(message);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExceptionType.Should().Be("TimeoutException");
    }

    [Fact]
    public async Task ExtractAsync_WithAuthenticationInErrorDescription_DetectsAuthenticationException()
    {
        var message = CreateMessage(1, deadLetterErrorDescription: "Authentication failed: invalid credentials");

        var result = await _sut.ExtractAsync(message);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExceptionType.Should().Be("AuthenticationException");
    }

    [Fact]
    public async Task ExtractBatchAsync_WithMultipleMessages_ReturnsFeaturesList()
    {
        var messages = new[]
        {
            CreateMessage(1, "Error1", "queue1"),
            CreateMessage(2, "Error2", "queue2"),
            CreateMessage(3, "Error3", "queue3"),
        };

        var result = await _sut.ExtractBatchAsync(messages);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].EntityName.Should().Be("queue1");
        result.Value[1].EntityName.Should().Be("queue2");
        result.Value[2].EntityName.Should().Be("queue3");
    }

    [Fact]
    public async Task ExtractAsync_WithNullMessage_Throws()
    {
        var act = () => _sut.ExtractAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractBatchAsync_WithNullList_Throws()
    {
        var act = () => _sut.ExtractBatchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
