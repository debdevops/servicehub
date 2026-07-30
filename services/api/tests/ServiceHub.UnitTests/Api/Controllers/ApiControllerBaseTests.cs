using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ServiceHub.Api.Controllers;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers;

/// <summary>
/// Tests the ApiControllerBase helper methods via a concrete derived class.
/// </summary>
public class ApiControllerBaseTests
{
    private sealed class TestController : ApiControllerBase
    {
        public IActionResult TestToActionResult(Result result) => ToActionResult(result);
        public ActionResult<T> TestToActionResultT<T>(Result<T> result) => ToActionResult(result);
        public ActionResult<T> TestToActionResultError<T>(Error error) => ToActionResult<T>(error);
        public ActionResult<T> TestToCreatedResult<T>(Result<T> result, string actionName, object? routeValues = null) => ToCreatedResult(result, actionName, routeValues);
        public ActionResult<T> TestToAcceptedResult<T>(Result<T> result) => ToAcceptedResult(result);

        public Task<Result<Namespace>> TestGetOwnedNamespaceAsync(
            INamespaceRepository repository, Guid namespaceId, CancellationToken cancellationToken = default) =>
            GetOwnedNamespaceAsync(repository, namespaceId, cancellationToken);

        public Task<Result<Namespace>> TestGetExclusivelyOwnedNamespaceAsync(
            INamespaceRepository repository, Guid namespaceId, CancellationToken cancellationToken = default) =>
            GetExclusivelyOwnedNamespaceAsync(repository, namespaceId, cancellationToken);
    }

    private static TestController CreateController(string? ownerId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (ownerId is not null)
        {
            httpContext.Items["OwnerId"] = ownerId;
        }

        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
        return controller;
    }

    private static Namespace CreateTestNamespace(string ownerId) =>
        Namespace.Create(
            "test-ns.servicebus.windows.net",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: ownerId).Value;

    // ── GetOwnedNamespaceAsync (owner OR shared) ────────────────────────────

    [Fact]
    public async Task GetOwnedNamespaceAsync_TrueOwner_ReturnsSuccess()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var controller = CreateController("owner-a");

        var result = await controller.TestGetOwnedNamespaceAsync(repo.Object, ns.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetOwnedNamespaceAsync_SharedOwner_ReturnsSuccess()
    {
        var ns = CreateTestNamespace("owner-a");
        ns.ShareWith("owner-b");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var controller = CreateController("owner-b");

        var result = await controller.TestGetOwnedNamespaceAsync(repo.Object, ns.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetOwnedNamespaceAsync_UnrelatedOwner_ReturnsNotFound()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var controller = CreateController("owner-c");

        var result = await controller.TestGetOwnedNamespaceAsync(repo.Object, ns.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ── GetExclusivelyOwnedNamespaceAsync (true owner only) ─────────────────

    [Fact]
    public async Task GetExclusivelyOwnedNamespaceAsync_TrueOwner_ReturnsSuccess()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var controller = CreateController("owner-a");

        var result = await controller.TestGetExclusivelyOwnedNamespaceAsync(repo.Object, ns.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetExclusivelyOwnedNamespaceAsync_SharedOwner_ReturnsNotFound()
    {
        // The critical guarantee this method exists for: shared access is NOT enough for
        // privilege-sensitive actions (delete, share, revoke).
        var ns = CreateTestNamespace("owner-a");
        ns.ShareWith("owner-b");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var controller = CreateController("owner-b");

        var result = await controller.TestGetExclusivelyOwnedNamespaceAsync(repo.Object, ns.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void ToActionResult_WhenSuccess_ShouldReturnNoContent()
    {
        var controller = CreateController();
        var result = controller.TestToActionResult(Result.Success());

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public void ToActionResult_WhenValidationError_ShouldReturnBadRequest()
    {
        var controller = CreateController();
        var error = Error.Validation("test.validation", "Validation failed");
        var result = controller.TestToActionResult(Result.Failure(error));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ToActionResult_WhenNotFoundError_ShouldReturnNotFound()
    {
        var controller = CreateController();
        var error = Error.NotFound("test.notfound", "Not found");
        var result = controller.TestToActionResult(Result.Failure(error));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void ToActionResult_WhenConflictError_ShouldReturnConflict()
    {
        var controller = CreateController();
        var error = Error.Conflict("test.conflict", "Conflict occurred");
        var result = controller.TestToActionResult(Result.Failure(error));

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void ToActionResultT_WhenSuccess_ShouldReturnOkWithValue()
    {
        var controller = CreateController();
        var successResult = Result<string>.Success("test-value");
        var result = controller.TestToActionResultT(successResult);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be("test-value");
    }

    [Fact]
    public void ToActionResultT_WhenFailure_ShouldReturnErrorResult()
    {
        var controller = CreateController();
        var error = Error.NotFound("test.notfound", "Not found");
        var failResult = Result<string>.Failure(error);
        var result = controller.TestToActionResultT(failResult);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void ToActionResultError_ShouldReturnMatchingStatusCode()
    {
        var controller = CreateController();
        var error = Error.Validation("test.val", "Bad input");
        var result = controller.TestToActionResultError<string>(error);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ToCreatedResult_WhenSuccess_ShouldReturnCreatedAtAction()
    {
        var controller = CreateController();
        var successResult = Result<string>.Success("new-item");
        var result = controller.TestToCreatedResult(successResult, "GetById", new { id = 1 });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = (CreatedAtActionResult)result.Result!;
        created.Value.Should().Be("new-item");
    }

    [Fact]
    public void ToCreatedResult_WhenFailure_ShouldReturnError()
    {
        var controller = CreateController();
        var error = Error.Validation("test.val", "Bad input");
        var failResult = Result<string>.Failure(error);
        var result = controller.TestToCreatedResult(failResult, "GetById");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ToAcceptedResult_WhenSuccess_ShouldReturnAccepted()
    {
        var controller = CreateController();
        var successResult = Result<string>.Success("accepted-item");
        var result = controller.TestToAcceptedResult(successResult);

        result.Result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public void ToAcceptedResult_WhenFailure_ShouldReturnError()
    {
        var controller = CreateController();
        var error = Error.Conflict("test.conflict", "Conflict");
        var failResult = Result<string>.Failure(error);
        var result = controller.TestToAcceptedResult(failResult);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Theory]
    [InlineData(typeof(UnauthorizedObjectResult))]
    public void ToActionResult_UnauthorizedError_ShouldReturnUnauthorized(Type expectedType)
    {
        var controller = CreateController();
        var error = Error.Unauthorized("test.unauth", "Unauthorized");
        var result = controller.TestToActionResult(Result.Failure(error));

        result.Should().BeOfType(expectedType);
    }

    [Fact]
    public void ToActionResult_ForbiddenError_ShouldReturn403()
    {
        var controller = CreateController();
        var error = Error.Forbidden("test.forbidden", "Forbidden");
        var result = controller.TestToActionResult(Result.Failure(error));

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ToActionResult_ProblemDetailsIncludesCode()
    {
        var controller = CreateController();
        var error = Error.Validation("my.error.code", "Some message");
        var result = controller.TestToActionResult(Result.Failure(error)) as BadRequestObjectResult;

        result.Should().NotBeNull();
        var pd = result!.Value.Should().BeOfType<ProblemDetails>().Subject;
        pd.Extensions.Should().ContainKey("code");
        pd.Extensions["code"].Should().Be("my.error.code");
    }

    [Fact]
    public void ToActionResult_ProblemDetailsIncludesTraceId()
    {
        var controller = CreateController();
        var error = Error.Validation("test", "msg");
        var result = controller.TestToActionResult(Result.Failure(error)) as BadRequestObjectResult;

        var pd = result!.Value.Should().BeOfType<ProblemDetails>().Subject;
        pd.Extensions.Should().ContainKey("traceId");
    }
}
