using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public sealed class ErrorHandlingMiddlewareTests
{
    private static (DefaultHttpContext Context, MemoryStream Body) CreateContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static ErrorHandlingMiddleware CreateMiddleware(
        RequestDelegate next,
        bool isDevelopment = false)
    {
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName)
            .Returns(isDevelopment ? Environments.Development : Environments.Production);
        mockEnv.Setup(e => e.ApplicationName).Returns("Test");
        mockEnv.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
        mockEnv.Setup(e => e.ContentRootFileProvider)
            .Returns(Mock.Of<Microsoft.Extensions.FileProviders.IFileProvider>());

        return new ErrorHandlingMiddleware(
            next,
            NullLogger<ErrorHandlingMiddleware>.Instance,
            mockEnv.Object);
    }

    private static string ReadBody(MemoryStream body)
    {
        body.Seek(0, SeekOrigin.Begin);
        return new StreamReader(body).ReadToEnd();
    }

    // ── No exception ─────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoException_Returns200()
    {
        var (context, _) = CreateContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    // ── Exception mappings ───────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        var (context, body) = CreateContext();
        var middleware = CreateMiddleware(_ => throw new ArgumentException("bad arg"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns401()
    {
        var (context, _) = CreateContext();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_TimeoutException_Returns504()
    {
        var (context, _) = CreateContext();
        var middleware = CreateMiddleware(_ => throw new TimeoutException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
        var (context, _) = CreateContext();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledException_AbortedRequest_Returns499()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (context, _) = CreateContext();
        context.RequestAborted = cts.Token;

        var middleware = CreateMiddleware(_ => throw new OperationCanceledException(cts.Token));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(499);
    }

    // ── Response body ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Exception_WritesJsonBody()
    {
        var (context, body) = CreateContext();
        var middleware = CreateMiddleware(_ => throw new ArgumentException("bad input"));

        await middleware.InvokeAsync(context);

        var json = ReadBody(body);
        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("code", out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Development_ResponseContentTypeIsJson()
    {
        var (context, _) = CreateContext();
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("dev error"),
            isDevelopment: true);

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Contain("application/json");
    }
}
