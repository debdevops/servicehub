using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class HeuristicAnalyserTests
{
    private static DlqMessage CreateMessage(
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        string? bodyPreview = null)
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
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            BodyPreview = bodyPreview
        };
    }

    [Fact]
    public void Evaluate_TimeoutKeyword_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Connection timeout after 30s");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
        hit.Confidence.Should().Be(0.80);
    }

    [Fact]
    public void Evaluate_TimedOut_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Request timed out");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
    }

    [Fact]
    public void Evaluate_SchemaValidation_ReturnsDataQuality()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Schema validation failed for property X");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.DataQuality);
        hit.Confidence.Should().Be(0.78);
    }

    [Fact]
    public void Evaluate_InvalidFormat_ReturnsDataQuality()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "invalid format for date field");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.DataQuality);
    }

    [Fact]
    public void Evaluate_UnauthorizedKeyword_ReturnsAuthorization()
    {
        // Note: this won't match deterministic (no "401") so heuristic catches it
        var msg = CreateMessage(deadLetterErrorDescription: "Access denied to the resource");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Authorization);
        hit.Confidence.Should().Be(0.82);
    }

    [Fact]
    public void Evaluate_PermissionDenied_ReturnsAuthorization()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "permission denied for user");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Authorization);
    }

    [Fact]
    public void Evaluate_NotFound_ReturnsResourceNotFound()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Resource not found in database");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ResourceNotFound);
        hit.Confidence.Should().Be(0.75);
    }

    [Fact]
    public void Evaluate_404_ReturnsResourceNotFound()
    {
        // No "unauthorized" or "forbidden" so deterministic won't match 403/401 rules
        var msg = CreateMessage(deadLetterErrorDescription: "HTTP 404 - resource missing");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ResourceNotFound);
    }

    [Fact]
    public void Evaluate_QuotaExceeded_ReturnsQuotaExceeded()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Entity full, quota exceeded");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.QuotaExceeded);
        hit.Confidence.Should().Be(0.78);
    }

    [Fact]
    public void Evaluate_SizeExceeded_ReturnsQuotaExceeded()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Message too large for queue");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.QuotaExceeded);
    }

    [Fact]
    public void Evaluate_DatabaseError_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Database connection pool exhausted");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
        hit.Confidence.Should().Be(0.70);
    }

    [Fact]
    public void Evaluate_ServiceUnavailable_ReturnsTransient()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Service unavailable, try later");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.Transient);
    }

    [Fact]
    public void Evaluate_GenericException_ReturnsProcessingError()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "An unhandled exception occurred");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ProcessingError);
        hit.Confidence.Should().Be(0.50);
    }

    [Fact]
    public void Evaluate_GenericError_ReturnsProcessingError()
    {
        var msg = CreateMessage(deadLetterErrorDescription: "Generic error in processing pipeline");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().NotBeNull();
        hit!.Category.Should().Be(FailureCategory.ProcessingError);
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsNull()
    {
        var msg = CreateMessage(deadLetterReason: "", deadLetterErrorDescription: "", bodyPreview: "");

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().BeNull();
    }

    [Fact]
    public void Evaluate_NullFields_ReturnsNull()
    {
        var msg = CreateMessage();

        var hit = HeuristicAnalyser.Evaluate(msg);

        hit.Should().BeNull();
    }
}
