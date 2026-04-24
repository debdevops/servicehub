using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class ForensicEngineTests
{
    private readonly ForensicEngine _sut = new(NullLogger<ForensicEngine>.Instance);

    // ── Helper ──────────────────────────────────────────────────────

    private static DlqMessage MakeMessage(
        string? reason = null,
        string? desc = null,
        string? bodyPreview = null,
        int deliveryCount = 1)
    {
        return new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = 1,
            BodyHash = "abc",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = reason,
            DeadLetterErrorDescription = desc,
            BodyPreview = bodyPreview,
            DeliveryCount = deliveryCount,
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Tier 1 — Deterministic classifier
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MaxDeliveryCountExceeded_Returns_MaxDelivery_0_99()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded", deliveryCount: 10);
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.MaxDelivery);
        result.Confidence.Should().Be(0.99);
        result.Tier.Should().Be("Deterministic");
    }

    [Fact]
    public void TTLExpiredException_Returns_Expired_0_99()
    {
        var msg = MakeMessage(reason: "TTLExpiredException");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Expired);
        result.Confidence.Should().Be(0.99);
        result.ReplaySafety.Should().Be("Unsafe");
    }

    [Fact]
    public void HeaderSizeExceeded_Returns_QuotaExceeded_0_98()
    {
        var msg = MakeMessage(reason: "HeaderSizeExceeded");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.QuotaExceeded);
        result.Confidence.Should().Be(0.98);
    }

    [Fact]
    public void MessageSizeExceeded_Returns_QuotaExceeded_0_98()
    {
        var msg = MakeMessage(reason: "MessageSizeExceeded");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.QuotaExceeded);
        result.Confidence.Should().Be(0.98);
    }

    [Fact]
    public void SessionFilter_Returns_ProcessingError_0_95()
    {
        var msg = MakeMessage(reason: "SessionFilterMismatch");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void Http401_Returns_Authorization_0_97()
    {
        var msg = MakeMessage(desc: "Consumer returned 401 Unauthorized");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Authorization);
        result.Confidence.Should().Be(0.97);
        result.ReplaySafety.Should().Be("Unsafe");
    }

    [Fact]
    public void Http403_Returns_Authorization_0_97()
    {
        var msg = MakeMessage(desc: "Consumer returned 403 Forbidden");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Authorization);
        result.Confidence.Should().Be(0.97);
    }

    [Fact]
    public void JsonDeserialization_Returns_DataQuality_0_95()
    {
        var msg = MakeMessage(desc: "System.Text.Json.JsonException: invalid payload");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.DataQuality);
        result.Confidence.Should().Be(0.95);
        result.ReplaySafety.Should().Be("Unsafe");
    }

    [Fact]
    public void ConnectionRefused_Returns_Transient_0_93()
    {
        var msg = MakeMessage(desc: "Connection refused by downstream");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
        result.Confidence.Should().Be(0.93);
    }

    [Fact]
    public void SqlTimeout_Returns_Transient_0_92()
    {
        var msg = MakeMessage(desc: "SqlTimeout exceeded during query");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
        result.Confidence.Should().Be(0.92);
    }

    [Fact]
    public void ExplicitDeadLetter_Returns_ProcessingError_0_90()
    {
        var msg = MakeMessage(reason: "BusinessRuleViolation", desc: "Unhandled exception in handler");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().Be(0.90);
    }

    // ═══════════════════════════════════════════════════════════════
    // Tier 2 — Heuristic analyser
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Heuristic_Timeout_Returns_Transient_0_80()
    {
        var msg = MakeMessage(bodyPreview: "operation timed out after 30s");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Transient);
        result.Confidence.Should().Be(0.80);
        result.Tier.Should().Be("Heuristic");
    }

    [Fact]
    public void Heuristic_Schema_Returns_DataQuality_0_78()
    {
        var msg = MakeMessage(bodyPreview: "schema validation error on field 'amount'");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.DataQuality);
        result.Confidence.Should().Be(0.78);
    }

    [Fact]
    public void Heuristic_NotFound_Returns_ResourceNotFound_0_75()
    {
        var msg = MakeMessage(bodyPreview: "resource not found for id 42");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ResourceNotFound);
        result.Confidence.Should().Be(0.75);
    }

    [Fact]
    public void Heuristic_GenericError_Returns_ProcessingError_0_50()
    {
        // "failed" is the catch-all pattern — no earlier pattern matches
        var msg = MakeMessage(bodyPreview: "processing step 3 failed");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.ProcessingError);
        result.Confidence.Should().Be(0.50);
        result.Tier.Should().Be("Heuristic");
    }

    // ═══════════════════════════════════════════════════════════════
    // Tier 3 — Unknown fallthrough
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NoMatch_Returns_Unknown_0_00()
    {
        var msg = MakeMessage(reason: null, desc: null, bodyPreview: "hello world 12345");
        var result = _sut.Analyse(msg);

        result.Category.Should().Be(FailureCategory.Unknown);
        result.Confidence.Should().Be(0.0);
        result.ReplaySafety.Should().Be("RequiresReview");
        result.Tier.Should().Be("None");
    }

    // ═══════════════════════════════════════════════════════════════
    // Replay-safety verdicts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Transient_LowDelivery_Returns_Safe()
    {
        var msg = MakeMessage(desc: "Connection refused by downstream", deliveryCount: 3);
        var result = _sut.Analyse(msg);

        result.ReplaySafety.Should().Be("Safe");
    }

    [Fact]
    public void Transient_HighDelivery_Returns_RequiresReview()
    {
        var msg = MakeMessage(desc: "Connection refused by downstream", deliveryCount: 15);
        var result = _sut.Analyse(msg);

        result.ReplaySafety.Should().Be("RequiresReview");
    }

    [Fact]
    public void MaxDelivery_HighCount_Returns_Unsafe()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded", deliveryCount: 10);
        var result = _sut.Analyse(msg);

        result.ReplaySafety.Should().Be("Unsafe");
    }

    // ═══════════════════════════════════════════════════════════════
    // IForensicEngine interface contract
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ForensicEngine_Implements_IForensicEngine()
    {
        _sut.Should().BeAssignableTo<IForensicEngine>();
    }
}
