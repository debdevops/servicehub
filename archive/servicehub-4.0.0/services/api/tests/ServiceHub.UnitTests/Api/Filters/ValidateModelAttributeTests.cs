using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using ServiceHub.Api.Filters;

namespace ServiceHub.UnitTests.Api.Filters;

public sealed class ValidateModelAttributeTests
{
    private static ActionExecutingContext CreateContext(bool addError = false)
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();

        if (addError)
        {
            modelState.AddModelError("Name", "Name is required.");
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            modelState);

        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?>(),
            new object());
    }

    [Fact]
    public void OnActionExecuting_ValidModel_DoesNotSetResult()
    {
        var context = CreateContext(addError: false);
        new ValidateModelAttribute().OnActionExecuting(context);
        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnActionExecuting_InvalidModel_SetsBadRequestResult()
    {
        var context = CreateContext(addError: true);
        new ValidateModelAttribute().OnActionExecuting(context);
        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void OnActionExecuting_InvalidModel_ResultContainsValidationProblemDetails()
    {
        var context = CreateContext(addError: true);
        new ValidateModelAttribute().OnActionExecuting(context);
        var result = context.Result as BadRequestObjectResult;
        result!.Value.Should().BeOfType<ValidationProblemDetails>();
    }

    [Fact]
    public void OnActionExecuting_InvalidModel_ProblemDetailsContainsErrors()
    {
        var context = CreateContext(addError: true);
        new ValidateModelAttribute().OnActionExecuting(context);
        var result = context.Result as BadRequestObjectResult;
        var problemDetails = result!.Value as ValidationProblemDetails;
        problemDetails!.Errors.Should().ContainKey("Name");
    }
}
