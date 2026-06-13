using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Infrastructure.Gcp.Models;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>
/// Tests for <see cref="GcpForensicEngine"/> and <see cref="GcpForensicExtensions"/>.
/// </summary>
public sealed class GcpForensicEngineTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullBaseEngine_Throws()
    {
        var act = () => new GcpForensicEngine(null!, NullLogger<GcpForensicEngine>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("baseEngine");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var act = () => new GcpForensicEngine(baseEngine.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var act = () => new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: null message
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NullMessage_Throws()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var act = () => sut.Analyse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: GCP rule hit → MaxDeliveryAttempts
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_MaxDeliveryAttemptsReason_ReturnsMaxDelivery()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterReason: "maxDeliveryAttempts");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
        result.Confidence.Should().BeApproximately(0.99, 0.01);
        result.ReplaySafety.Should().Be("ManualReviewRequired");
        result.Tier.Should().Be("GCP-Deterministic");
        baseEngine.Verify(e => e.Analyse(It.IsAny<DlqMessage>()), Times.Never);
    }

    [Fact]
    public void Analyse_CloudPubSubDeadLetterSourceInProps_ReturnsMaxDelivery()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(
            propsJson: """{"CloudPubSubDeadLetterSourceSubscription":"projects/p/subscriptions/s"}""");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
        result.Tier.Should().Be("GCP-Deterministic");
    }

    [Fact]
    public void Analyse_MaxDeliveryAttemptsInDescription_ReturnsMaxDelivery()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterErrorDescription: "exceeded max delivery attempts");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: GCP rule hit → AckDeadline
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_AckDeadlineWithMultipleDeliveries_ReturnsTransient()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(
            deadLetterErrorDescription: "ack_deadline expired",
            deliveryCount: 3);

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
        result.Confidence.Should().BeApproximately(0.87, 0.01);
        result.Tier.Should().Be("GCP-Deterministic");
    }

    [Fact]
    public void Analyse_AckDeadlineSecondsInProps_ReturnsTransient()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(
            propsJson: """{"ackDeadlineSeconds":60}""",
            deliveryCount: 2);

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: GCP rule hit → Nack
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NackReason_ReturnsProcessingError()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterReason: "nack");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().BeApproximately(0.91, 0.01);
        result.Tier.Should().Be("GCP-Deterministic");
    }

    [Fact]
    public void Analyse_SubscriberNackedInDescription_ReturnsProcessingError()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterErrorDescription: "subscriber nacked the message");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: GCP rule hit → OrderingKey stall
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_OrderingKeyInProps_ReturnsProcessingError()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(propsJson: """{"orderingKey":"customer-123"}""");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().BeApproximately(0.82, 0.01);
        result.Tier.Should().Be("GCP-Deterministic");
    }

    [Fact]
    public void Analyse_OrderingInReason_ReturnsProcessingError()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterReason: "ordering-key-blocked");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicEngine — Analyse: no rule fires → delegates to base
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NoGcpRuleMatches_DelegatesToBaseEngine()
    {
        var expectedResult = new ForensicEngineResult(
            FailureCategory.Unknown, 0.1, "Base root cause", "Safe", "Base-Heuristic");

        var baseEngine = new Mock<IForensicEngine>();
        baseEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(expectedResult);

        var sut = new GcpForensicEngine(baseEngine.Object, NullLogger<GcpForensicEngine>.Instance);

        var msg = BuildMessage();

        var result = sut.Analyse(msg);

        result.Should().Be(expectedResult);
        baseEngine.Verify(e => e.Analyse(msg), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpForensicExtensions — Evaluate: null throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GcpExtensions_Evaluate_NullMessage_Throws()
    {
        var act = () => GcpForensicExtensions.Evaluate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GcpExtensions_Evaluate_EmptyMessage_ReturnsNull()
    {
        var msg = BuildMessage();
        var hit = GcpForensicExtensions.Evaluate(msg);
        hit.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GcpOrderingKeyInfo model tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GcpOrderingKeyInfo_Constructor_SetsAllProperties()
    {
        var info = new GcpOrderingKeyInfo("customer-123", 5, true);

        info.OrderingKey.Should().Be("customer-123");
        info.DeliveryAttempts.Should().Be(5);
        info.IsStalled.Should().BeTrue();
    }

    [Fact]
    public void GcpOrderingKeyInfo_RecordEquality_WorksCorrectly()
    {
        var info1 = new GcpOrderingKeyInfo("key-1", 3, false);
        var info2 = new GcpOrderingKeyInfo("key-1", 3, false);
        var info3 = new GcpOrderingKeyInfo("key-2", 3, false);

        info1.Should().Be(info2);
        info1.Should().NotBe(info3);
    }

    [Fact]
    public void GcpOrderingKeyInfo_NotStalled_IsCorrect()
    {
        var info = new GcpOrderingKeyInfo("key-abc", 1, false);
        info.IsStalled.Should().BeFalse();
        info.DeliveryAttempts.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static DlqMessage BuildMessage(
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        string? propsJson = null,
        int deliveryCount = 1)
    {
        return new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = 1,
            BodyHash = "abc",
            NamespaceId = Guid.NewGuid(),
            OwnerId = "owner",
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            BodyPreview = "{}",
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            ApplicationPropertiesJson = propsJson,
            DeliveryCount = deliveryCount
        };
    }
}
