using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Tests for <see cref="AwsForensicEngine"/> and <see cref="AwsForensicExtensions"/>.
/// </summary>
public sealed class AwsForensicEngineTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullBaseEngine_Throws()
    {
        var act = () => new AwsForensicEngine(null!, NullLogger<AwsForensicEngine>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("baseEngine");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var act = () => new AwsForensicEngine(baseEngine.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var act = () => new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: null message
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NullMessage_Throws()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var act = () => sut.Analyse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: AWS rule hit → MaxDelivery
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_MaxReceiveCountReason_ReturnsMaxDeliveryWithoutCallingBaseEngine()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterReason: "MaxReceiveCount");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
        result.Confidence.Should().BeApproximately(0.99, 0.01);
        result.ReplaySafety.Should().Be("ManualReviewRequired");
        result.Tier.Should().Be("AWS-Deterministic");
        baseEngine.Verify(e => e.Analyse(It.IsAny<DlqMessage>()), Times.Never);
    }

    [Fact]
    public void Analyse_MaxReceiveCountInDescription_ReturnsMaxDelivery()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterErrorDescription: "exceeded maxReceiveCount limit");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
        result.Tier.Should().Be("AWS-Deterministic");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: AWS rule hit → Lambda error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_LambdaRequestIdAndErrorCode_ReturnsProcessingError()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var propsJson = """{"RequestID":"abc-123","ErrorCode":"TimeoutError"}""";
        var msg = BuildMessage(propsJson: propsJson);

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().BeApproximately(0.93, 0.01);
        result.Tier.Should().Be("AWS-Deterministic");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: AWS rule hit → KMS error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_KmsInDescription_ReturnsAuthorization()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterErrorDescription: "KMS key access denied");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Authorization);
        result.Confidence.Should().BeApproximately(0.97, 0.01);
        result.Tier.Should().Be("AWS-Deterministic");
    }

    [Fact]
    public void Analyse_KmsDecryptInDescription_ReturnsAuthorization()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage(deadLetterErrorDescription: "kms:Decrypt permission missing");

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Authorization);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: AWS rule hit → VisibilityTimeout
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_VisibilityTimeoutWithMultipleDeliveries_ReturnsTransient()
    {
        var baseEngine = new Mock<IForensicEngine>();
        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage(
            propsJson: """{"VisibilityTimeout":"30"}""",
            deliveryCount: 3);

        var result = sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
        result.Confidence.Should().BeApproximately(0.85, 0.01);
        result.Tier.Should().Be("AWS-Deterministic");
    }

    [Fact]
    public void Analyse_VisibilityTimeoutWithSingleDelivery_FallsThroughToBaseEngine()
    {
        var expectedResult = new ForensicEngineResult(
            FailureCategory.Unknown, 0.5, "unknown", "Safe", "Base");

        var baseEngine = new Mock<IForensicEngine>();
        baseEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(expectedResult);

        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        // DeliveryCount == 1, so VisibilityTimeout rule won't fire
        var msg = BuildMessage(
            propsJson: """{"VisibilityTimeout":"30"}""",
            deliveryCount: 1);

        var result = sut.Analyse(msg);

        result.Should().Be(expectedResult);
        baseEngine.Verify(e => e.Analyse(msg), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicEngine — Analyse: no rule fires → delegates to base
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NoAwsRuleMatches_DelegatesToBaseEngine()
    {
        var expectedResult = new ForensicEngineResult(
            FailureCategory.Unknown, 0.1, "Base root cause", "Safe", "Base-Heuristic");

        var baseEngine = new Mock<IForensicEngine>();
        baseEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(expectedResult);

        var sut = new AwsForensicEngine(baseEngine.Object, NullLogger<AwsForensicEngine>.Instance);

        var msg = BuildMessage();

        var result = sut.Analyse(msg);

        result.Should().Be(expectedResult);
        baseEngine.Verify(e => e.Analyse(msg), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicExtensions — Evaluate: null throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AwsExtensions_Evaluate_NullMessage_Throws()
    {
        var act = () => AwsForensicExtensions.Evaluate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicExtensions — Evaluate: all null props → no hit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AwsExtensions_Evaluate_EmptyMessage_ReturnsNull()
    {
        var msg = BuildMessage();
        var hit = AwsForensicExtensions.Evaluate(msg);
        hit.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AwsForensicExtensions — Lambda ErrorCode extraction
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AwsExtensions_LambdaRule_ExtractsErrorCodeFromJson()
    {
        var propsJson = """{"RequestID":"req-001","ErrorCode":"Lambda.Timeout"}""";
        var msg = BuildMessage(propsJson: propsJson);

        var hit = AwsForensicExtensions.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.RootCause.Should().Contain("Lambda.Timeout");
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
