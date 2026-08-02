using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class SignatureReplayControllerTests
{
    private readonly Mock<ISignatureReplayService> _serviceMock = new();
    private readonly Mock<ILogger<SignatureReplayController>> _loggerMock = new();
    private readonly SignatureReplayController _controller;
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string SignatureHash = "abc123";

    public SignatureReplayControllerTests()
    {
        _controller = new SignatureReplayController(_serviceMock.Object, NoOpAuditLogger.Instance, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private void SetIntentHeaders(string intent)
    {
        _controller.HttpContext.Request.Headers[IntentHeaders.IntentHeaderName] = intent;
        _controller.HttpContext.Request.Headers[IntentHeaders.ConfirmHeaderName] = "true";
    }

    /// <summary>
    /// <c>ApiControllerBase.ToActionResult&lt;T&gt;</c> wraps successes as <c>Ok(value)</c>
    /// (an <c>OkObjectResult</c> on <c>.Result</c>), not a directly-assigned <c>.Value</c> —
    /// unwrap either shape the same way a real HTTP round-trip would.
    /// </summary>
    private static T GetOkValue<T>(ActionResult<T> result) =>
        result.Value ?? (T)((OkObjectResult)result.Result!).Value!;

    private static BulkOperationJobResponse SampleJobResponse(Guid namespaceId, Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        OperationType: "Replay",
        Status: nameof(BulkOperationStatus.Pending),
        NamespaceId: namespaceId,
        NamespaceDisplayName: "ns",
        EntityNameFilter: null,
        StatusFilter: null,
        CategoryFilter: null,
        From: null,
        To: null,
        TotalMatched: 5,
        ProcessedCount: 0,
        SuccessCount: 0,
        FailureCount: 0,
        SkippedCount: 0,
        FailureSample: null,
        ErrorSummary: null,
        CreatedAt: DateTimeOffset.UtcNow,
        StartedAt: null,
        CompletedAt: null,
        IsCancellable: true);

    // ── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_WithoutIntentHeaders_Returns428()
    {
        var result = await _controller.Start(_namespaceId, SignatureHash, new SignatureReplayScopeRequest(null, null, null));

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        _serviceMock.Verify(
            s => s.StartAsync(It.IsAny<string>(), It.IsAny<SignatureReplayStartRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_WithIntentHeaders_CallsServiceAndReturns202()
    {
        SetIntentHeaders(IntentHeaders.IntentSignatureReplay);
        var response = SampleJobResponse(_namespaceId);
        _serviceMock
            .Setup(s => s.StartAsync(
                It.IsAny<string>(),
                It.Is<SignatureReplayStartRequest>(r =>
                    r.Filter.NamespaceId == _namespaceId && r.Filter.SignatureHash == SignatureHash),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Start(_namespaceId, SignatureHash, new SignatureReplayScopeRequest(null, null, null));

        result.Result.Should().BeOfType<AcceptedAtActionResult>()
            .Which.Value.Should().Be(response);
    }

    [Fact]
    public async Task Start_ServiceRejectsRequest_ReturnsFailureActionResult()
    {
        SetIntentHeaders(IntentHeaders.IntentSignatureReplay);
        _serviceMock
            .Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<SignatureReplayStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Failure(
                Error.Validation("SignatureReplay.NoMatches", "No DLQ messages match this signature and filter.")));

        var result = await _controller.Start(_namespaceId, SignatureHash, new SignatureReplayScopeRequest(null, null, null));

        result.Result.Should().NotBeOfType<AcceptedAtActionResult>();
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_DoesNotRequireIntentHeaders_CallsServiceDirectly()
    {
        var response = new BulkOperationPreviewResponse(5, [], true, [], 0);
        _serviceMock
            .Setup(s => s.PreviewAsync(
                It.IsAny<string>(),
                It.Is<SignatureReplayPreviewRequest>(r =>
                    r.Filter.NamespaceId == _namespaceId && r.Filter.SignatureHash == SignatureHash),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationPreviewResponse>.Success(response));

        var result = await _controller.Preview(_namespaceId, SignatureHash, new SignatureReplayScopeRequest(null, null, null));

        GetOkValue(result).Should().Be(response);
    }

    // ── GetJob ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJob_DelegatesToService()
    {
        var jobId = Guid.NewGuid();
        var response = SampleJobResponse(_namespaceId, jobId);
        _serviceMock
            .Setup(s => s.GetJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.GetJob(jobId);

        GetOkValue(result).Should().Be(response);
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_DoesNotRequireIntentHeaders_DelegatesToService()
    {
        var jobId = Guid.NewGuid();
        var response = SampleJobResponse(_namespaceId, jobId) with { Status = nameof(BulkOperationStatus.Cancelled) };
        _serviceMock
            .Setup(s => s.CancelJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Cancel(jobId);

        GetOkValue(result).Status.Should().Be(nameof(BulkOperationStatus.Cancelled));
    }
}
