using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Controllers;
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
    }

    private static TestController CreateController()
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
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
