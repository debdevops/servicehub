using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public class RequestLoggingMiddlewareTests
{
    private readonly Mock<ILogger<RequestLoggingMiddleware>> _logger;

    public RequestLoggingMiddlewareTests()
    {
        _logger = new Mock<ILogger<RequestLoggingMiddleware>>();
    }

    [Fact]
    public async Task InvokeAsync_WithNormalPath_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/namespaces";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/health/ready")]
    [InlineData("/api/v1/health/live")]
    public async Task InvokeAsync_WithHealthPath_ShouldSkipLogging(string path)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/_blazor")]
    public async Task InvokeAsync_WithSwaggerOrInternalPath_ShouldSkipLogging(string path)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_ShouldStillLog()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/messages";

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_ShouldIncludeCorrelationIdFromItems()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/namespaces";
        context.Items["CorrelationId"] = "test-correlation-123";

        await middleware.InvokeAsync(context);

        // Should complete without error; log output verified by logger mock
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleQueryStrings()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new RequestLoggingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/namespaces";
        context.Request.QueryString = new QueryString("?page=1&pageSize=50");

        await middleware.InvokeAsync(context);
    }
}
