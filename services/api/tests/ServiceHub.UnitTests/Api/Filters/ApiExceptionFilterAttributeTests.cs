using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ServiceHub.Api.Filters;

namespace ServiceHub.UnitTests.Api.Filters;

public sealed class ApiExceptionFilterAttributeTests
{
    private static ExceptionContext CreateContext(Exception exception, bool isDevelopment = false, bool requestAborted = false)
    {
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName)
            .Returns(isDevelopment ? Environments.Development : Environments.Production);
        mockEnv.Setup(e => e.ApplicationName).Returns("Test");
        mockEnv.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
        mockEnv.Setup(e => e.ContentRootFileProvider)
            .Returns(Mock.Of<Microsoft.Extensions.FileProviders.IFileProvider>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(mockEnv.Object);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        if (requestAborted)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            httpContext.RequestAborted = cts.Token;
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ExceptionContext(actionContext, []) { Exception = exception };
    }

    private static int? GetStatusCode(ExceptionContext context)
    {
        return context.Result is ObjectResult objectResult ? objectResult.StatusCode : null;
    }

    // ── Exception mapping ────────────────────────────────────────────

    [Fact]
    public void OnException_ArgumentNullException_Sets400()
    {
        var context = CreateContext(new ArgumentNullException("param"));
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void OnException_ArgumentException_Sets400()
    {
        var context = CreateContext(new ArgumentException("bad arg"));
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void OnException_UnauthorizedAccessException_Sets401()
    {
        var context = CreateContext(new UnauthorizedAccessException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void OnException_KeyNotFoundException_Sets404()
    {
        var context = CreateContext(new KeyNotFoundException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void OnException_InvalidOperationException_Sets409()
    {
        var context = CreateContext(new InvalidOperationException("conflict"));
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void OnException_TimeoutException_Sets504()
    {
        var context = CreateContext(new TimeoutException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    [Fact]
    public void OnException_NotImplementedException_Sets501()
    {
        var context = CreateContext(new NotImplementedException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public void OnException_NotSupportedException_Sets501()
    {
        var context = CreateContext(new NotSupportedException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public void OnException_UnknownException_Sets500()
    {
        var context = CreateContext(new DivideByZeroException());
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status500InternalServerError);
    }

    // ── Result type and ExceptionHandled ─────────────────────────────

    [Fact]
    public void OnException_SetsExceptionHandled()
    {
        var context = CreateContext(new InvalidOperationException());
        new ApiExceptionFilterAttribute().OnException(context);
        context.ExceptionHandled.Should().BeTrue();
    }

    [Fact]
    public void OnException_ResultIsProblemDetails()
    {
        var context = CreateContext(new ArgumentException("test"));
        new ApiExceptionFilterAttribute().OnException(context);
        var result = context.Result as ObjectResult;
        result!.Value.Should().BeOfType<ProblemDetails>();
    }

    // ── Client-cancelled requests (flood-scale finding) ──────────────
    //
    // Under a high-namespace-count flood, many concurrent in-flight requests get cancelled by
    // the client (navigation, connection-pool pressure). ErrorHandlingMiddleware already treats
    // this correctly (Debug log, 499, no body). Before this fix, this filter — which intercepts
    // OperationCanceledException first, inside the MVC pipeline — logged it as a full-stack-trace
    // ERROR and mapped it to 400, both wrong for a client that has already disconnected.

    [Fact]
    public void OnException_OperationCanceledException_RequestAborted_Sets499()
    {
        var context = CreateContext(new TaskCanceledException(), requestAborted: true);
        new ApiExceptionFilterAttribute().OnException(context);
        context.HttpContext.Response.StatusCode.Should().Be(499);
    }

    [Fact]
    public void OnException_OperationCanceledException_RequestAborted_SetsExceptionHandledWithoutWritingProblemDetails()
    {
        var context = CreateContext(new TaskCanceledException(), requestAborted: true);
        new ApiExceptionFilterAttribute().OnException(context);
        context.ExceptionHandled.Should().BeTrue();
        context.Result.Should().BeNull("a disconnected client has nothing to read a response body from");
    }

    [Fact]
    public void OnException_OperationCanceledException_RequestNotAborted_StillMapsAsBeforeNotAsClientCancellation()
    {
        // Only a genuinely client-aborted request gets the 499 short-circuit — an
        // OperationCanceledException from some other cause (e.g. a server-side timeout token,
        // request still live) must keep going through the normal mapping path.
        var context = CreateContext(new OperationCanceledException(), requestAborted: false);
        new ApiExceptionFilterAttribute().OnException(context);
        GetStatusCode(context).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void OnException_AlreadyHandled_DoesNotOverride()
    {
        var context = CreateContext(new ArgumentException("test"));
        context.ExceptionHandled = true;
        var originalResult = context.Result;

        new ApiExceptionFilterAttribute().OnException(context);

        context.Result.Should().Be(originalResult);
    }
}
