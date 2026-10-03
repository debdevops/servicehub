using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class MessageFeaturesTests
{
    private static DlqMessage BuildMessage(
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        string? bodyPreview = null,
        string? applicationPropertiesJson = null,
        string? contentType = null,
        int deliveryCount = 3,
        long messageSize = 1024,
        DateTimeOffset? enqueuedTimeUtc = null,
        DateTimeOffset? deadLetterTimeUtc = null,
        DateTimeOffset? detectedAtUtc = null,
        CloudProviderType cloudProvider = CloudProviderType.Azure,
        string entityName = "orders-queue")
    {
        var enqueued = enqueuedTimeUtc ?? new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);
        return new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = enqueued,
            DeadLetterTimeUtc = deadLetterTimeUtc ?? enqueued.AddSeconds(30),
            DetectedAtUtc = detectedAtUtc ?? enqueued.AddSeconds(45),
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            BodyPreview = bodyPreview,
            ApplicationPropertiesJson = applicationPropertiesJson,
            ContentType = contentType,
            DeliveryCount = deliveryCount,
            MessageSize = messageSize,
            CloudProvider = cloudProvider,
        };
    }

    [Fact]
    public void ErrorTextNormalised_DiffersOnlyByGuidTimestampAndOrderId_ProducesIdenticalText()
    {
        var msg1 = BuildMessage(
            deadLetterReason: "ProcessingError",
            deadLetterErrorDescription: "Failed to process order 48213 at 2026-03-10T14:30:00Z, correlation 3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var msg2 = BuildMessage(
            deadLetterReason: "ProcessingError",
            deadLetterErrorDescription: "Failed to process order 91007 at 2026-05-02T09:15:22Z, correlation a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        var features1 = SignalExtractor.ExtractFeatures(msg1);
        var features2 = SignalExtractor.ExtractFeatures(msg2);

        features1.ErrorTextNormalised.Should().Be(features2.ErrorTextNormalised);
        features1.ErrorTextNormalised.Should().Contain("<num>").And.Contain("<timestamp>").And.Contain("<guid>");
    }

    [Fact]
    public void ErrorTextNormalised_GenuinelyDifferentExceptions_ProducesDifferentText()
    {
        var msg1 = BuildMessage(
            deadLetterReason: "ProcessingError",
            deadLetterErrorDescription: "System.NullReferenceException: Object reference not set to an instance of an object.");
        var msg2 = BuildMessage(
            deadLetterReason: "ProcessingError",
            deadLetterErrorDescription: "System.Data.SqlClient.SqlException: Timeout expired.");

        var features1 = SignalExtractor.ExtractFeatures(msg1);
        var features2 = SignalExtractor.ExtractFeatures(msg2);

        features1.ErrorTextNormalised.Should().NotBe(features2.ErrorTextNormalised);
    }

    [Theory]
    [InlineData("{\"orderId\": 123, \"customer\": \"acme\"}", "json_object")]
    [InlineData("[1, 2, 3]", "json_array")]
    [InlineData("<order><id>123</id></order>", "xml")]
    [InlineData("plain text body, nothing structured here", "text")]
    public void PayloadShape_DetectsCorrectShape(string body, string expectedShape)
    {
        var msg = BuildMessage(bodyPreview: body);

        var features = SignalExtractor.ExtractFeatures(msg);

        features.PayloadShape.Should().Be(expectedShape);
    }

    [Fact]
    public void PayloadShape_BinaryControlCharacters_DetectedAsBinary()
    {
        var msg = BuildMessage(bodyPreview: "\u0001\u0002\u0003binarydata");

        var features = SignalExtractor.ExtractFeatures(msg);

        features.PayloadShape.Should().Be("binary");
    }

    [Fact]
    public void SchemaFingerprint_StableAcrossPropertyReordering()
    {
        var msg1 = BuildMessage(bodyPreview: "{\"orderId\": 123, \"customer\": \"acme\", \"total\": 42.5}");
        var msg2 = BuildMessage(bodyPreview: "{\"total\": 42.5, \"orderId\": 999, \"customer\": \"other\"}");

        var features1 = SignalExtractor.ExtractFeatures(msg1);
        var features2 = SignalExtractor.ExtractFeatures(msg2);

        features1.SchemaFingerprint.Should().Be(features2.SchemaFingerprint);
    }

    [Fact]
    public void SchemaFingerprint_DifferentPropertyNames_ProducesDifferentFingerprint()
    {
        var msg1 = BuildMessage(bodyPreview: "{\"orderId\": 123}");
        var msg2 = BuildMessage(bodyPreview: "{\"shipmentId\": 123}");

        var features1 = SignalExtractor.ExtractFeatures(msg1);
        var features2 = SignalExtractor.ExtractFeatures(msg2);

        features1.SchemaFingerprint.Should().NotBe(features2.SchemaFingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not valid json")]
    [InlineData("[1, 2,")]
    public void ExtractFeatures_NullEmptyOrMalformedBody_DoesNotThrow(string? body)
    {
        var msg = BuildMessage(bodyPreview: body);

        var act = () => SignalExtractor.ExtractFeatures(msg);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExtractFeatures_MalformedApplicationPropertiesJson_DoesNotThrowAndCountsZero()
    {
        var msg = BuildMessage(applicationPropertiesJson: "{not valid json");

        var features = SignalExtractor.ExtractFeatures(msg);

        features.PropertyCount.Should().Be(0);
    }

    [Fact]
    public void ExtractFeatures_FullyPopulatedMessage_AllNumericFeaturesPresent()
    {
        var enqueued = new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);
        var deadLettered = enqueued.AddSeconds(120);
        var detected = enqueued.AddSeconds(200);

        var msg = BuildMessage(
            deliveryCount: 7,
            messageSize: 2048,
            enqueuedTimeUtc: enqueued,
            deadLetterTimeUtc: deadLettered,
            detectedAtUtc: detected,
            applicationPropertiesJson: "{\"a\": 1, \"b\": 2, \"c\": 3}");

        var features = SignalExtractor.ExtractFeatures(msg);

        features.DeliveryCount.Should().Be(7);
        features.BodySizeBytes.Should().Be(2048);
        features.TimeToDeadletterSeconds.Should().Be(120);
        features.SecondsSinceEnqueued.Should().Be(200);
        features.HourOfDay.Should().Be(14);
        features.DayOfWeek.Should().Be((int)enqueued.UtcDateTime.DayOfWeek);
        features.PropertyCount.Should().Be(3);
        features.FeatureVersion.Should().Be(1);
    }

    [Fact]
    public void ExtractFeatures_NoDeadLetterTime_TimeToDeadletterSecondsIsZero()
    {
        var enqueued = new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.Zero);
        var noDeadLetter = new DlqMessage
        {
            MessageId = "msg-2",
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "orders-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = enqueued,
            DeadLetterTimeUtc = null,
            DetectedAtUtc = enqueued.AddSeconds(10),
        };

        var features = SignalExtractor.ExtractFeatures(noDeadLetter);

        features.TimeToDeadletterSeconds.Should().Be(0);
    }

    [Fact]
    public void ExtractFeatures_Provider_And_EntityName_MapDirectly()
    {
        var msg = BuildMessage(cloudProvider: CloudProviderType.Aws, entityName: "my-sqs-queue");

        var features = SignalExtractor.ExtractFeatures(msg);

        features.Provider.Should().Be(CloudProviderType.Aws);
        features.EntityName.Should().Be("my-sqs-queue");
    }
}
