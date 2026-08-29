using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class BulkOperationsControllerTests
{
    private readonly Mock<IBulkOperationService> _serviceMock = new();
    private readonly Mock<IGovernanceAccessEvaluator> _governanceAccessEvaluator = new();
    private readonly Mock<ILogger<BulkOperationsController>> _loggerMock = new();
    private readonly BulkOperationsController _controller;
    private readonly Guid _namespaceId = Guid.NewGuid();

    public BulkOperationsControllerTests()
    {
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GovernanceRole>(),
                It.IsAny<Guid?>(), It.IsAny<PillarKind?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _controller = new BulkOperationsController(
            _serviceMock.Object, _governanceAccessEvaluator.Object, NoOpAuditLogger.Instance, _loggerMock.Object)
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

    private static BulkOperationFilterRequest Filter(Guid namespaceId) =>
        new(namespaceId, EntityName: null, From: null, To: null, Status: DlqMessageStatus.Active, Category: null);

    private static BulkOperationJobResponse SampleJobResponse(Guid namespaceId, Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        OperationType: nameof(BulkOperationType.Replay),
        Status: nameof(BulkOperationStatus.Pending),
        NamespaceId: namespaceId,
        NamespaceDisplayName: "ns",
        EntityNameFilter: null,
        StatusFilter: nameof(DlqMessageStatus.Active),
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

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_InsufficientGovernanceRole_ReturnsForbidden()
    {
        SetIntentHeaders(IntentHeaders.IntentBulkReplay);
        var request = new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId));
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(), GovernanceRole.Operator,
                _namespaceId, PillarKind.Recover, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Forbidden("Governance.InsufficientRole", "denied")));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _serviceMock.Verify(
            s => s.CreateJobAsync(It.IsAny<string>(), It.IsAny<BulkOperationCreateRequest>(), It.IsAny<string?>(), It.IsAny<RecoveryActor>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WithoutIntentHeaders_Returns428()
    {
        var request = new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        _serviceMock.Verify(
            s => s.CreateJobAsync(It.IsAny<string>(), It.IsAny<BulkOperationCreateRequest>(), It.IsAny<string?>(), It.IsAny<RecoveryActor>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_ReplayWithReplayIntent_CallsServiceAndReturns202()
    {
        SetIntentHeaders(IntentHeaders.IntentBulkReplay);
        var request = new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId));
        var response = SampleJobResponse(_namespaceId);
        _serviceMock
            .Setup(s => s.CreateJobAsync(It.IsAny<string>(), request, It.IsAny<string?>(), It.IsAny<RecoveryActor>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<AcceptedAtActionResult>()
            .Which.Value.Should().Be(response);
    }

    [Fact]
    public async Task Create_PurgeWithReplayIntent_Returns428_WrongIntentDoesNotSubstitute()
    {
        SetIntentHeaders(IntentHeaders.IntentBulkReplay);
        var request = new BulkOperationCreateRequest(BulkOperationType.Purge, Filter(_namespaceId));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task Create_PurgeWithPurgeIntent_CallsServiceAndReturns202()
    {
        SetIntentHeaders(IntentHeaders.IntentBulkPurge);
        var request = new BulkOperationCreateRequest(BulkOperationType.Purge, Filter(_namespaceId));
        var response = SampleJobResponse(_namespaceId) with { OperationType = nameof(BulkOperationType.Purge) };
        _serviceMock
            .Setup(s => s.CreateJobAsync(It.IsAny<string>(), request, It.IsAny<string?>(), It.IsAny<RecoveryActor>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<AcceptedAtActionResult>();
    }

    [Fact]
    public async Task Create_ServiceRejectsRequest_ReturnsFailureActionResult()
    {
        SetIntentHeaders(IntentHeaders.IntentBulkReplay);
        var request = new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId));
        _serviceMock
            .Setup(s => s.CreateJobAsync(It.IsAny<string>(), request, It.IsAny<string?>(), It.IsAny<RecoveryActor>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Failure(
                Error.Validation("BulkOperation.NoMatches", "No DLQ messages match this filter.")));

        var result = await _controller.Create(request);

        result.Result.Should().NotBeOfType<AcceptedAtActionResult>();
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_DoesNotRequireIntentHeaders_CallsServiceDirectly()
    {
        var request = new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId));
        var response = new BulkOperationPreviewResponse(5, [], true, [], 0);
        _serviceMock
            .Setup(s => s.PreviewAsync(It.IsAny<string>(), request, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationPreviewResponse>.Success(response));

        var result = await _controller.Preview(request);

        GetOkValue(result).Should().Be(response);
    }

    // ── Get / List ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DelegatesToService()
    {
        var jobId = Guid.NewGuid();
        var response = SampleJobResponse(_namespaceId, jobId);
        _serviceMock
            .Setup(s => s.GetJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Get(jobId);

        GetOkValue(result).Should().Be(response);
    }

    [Fact]
    public async Task List_DelegatesToServiceWithPagingParams()
    {
        var page = new PaginatedResponse<BulkOperationJobResponse>([], 0, 1, 20, false, false);
        _serviceMock
            .Setup(s => s.ListJobsAsync(It.IsAny<string>(), _namespaceId, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaginatedResponse<BulkOperationJobResponse>>.Success(page));

        var result = await _controller.List(_namespaceId, page: 2, pageSize: 10);

        GetOkValue(result).Should().Be(page);
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_DoesNotRequireIntentHeaders_DelegatesToService()
    {
        var jobId = Guid.NewGuid();
        var job = SampleJobResponse(_namespaceId, jobId);
        var response = job with { Status = nameof(BulkOperationStatus.Cancelled) };
        _serviceMock
            .Setup(s => s.GetJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(job));
        _serviceMock
            .Setup(s => s.CancelJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(response));

        var result = await _controller.Cancel(jobId);

        GetOkValue(result).Status.Should().Be(nameof(BulkOperationStatus.Cancelled));
    }

    [Fact]
    public async Task Cancel_InsufficientGovernanceRole_ReturnsForbidden()
    {
        var jobId = Guid.NewGuid();
        var job = SampleJobResponse(_namespaceId, jobId);
        _serviceMock
            .Setup(s => s.GetJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Success(job));
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(), GovernanceRole.Operator,
                _namespaceId, PillarKind.Recover, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Forbidden("Governance.InsufficientRole", "denied")));

        var result = await _controller.Cancel(jobId);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _serviceMock.Verify(s => s.CancelJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_JobNotFound_ReturnsNotFoundWithoutGovernanceCheck()
    {
        var jobId = Guid.NewGuid();
        _serviceMock
            .Setup(s => s.GetJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationJobResponse>.Failure(
                Error.NotFound("BulkOperation.NotFound", "not found")));

        var result = await _controller.Cancel(jobId);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _serviceMock.Verify(s => s.CancelJobAsync(It.IsAny<string>(), jobId, It.IsAny<CancellationToken>()), Times.Never);
    }
}
