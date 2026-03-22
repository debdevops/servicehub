using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public class RateLimitingMiddlewareTests
{
    private readonly Mock<ILogger<RateLimitingMiddleware>> _logger;

    public RateLimitingMiddlewareTests()
    {
        _logger = new Mock<ILogger<RateLimitingMiddleware>>();
    }

    [Fact]
    public async Task InvokeAsync_UnderLimit_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new RateLimitingMiddleware(next, _logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task InvokeAsync_OverLimit_ShouldReturnTooManyRequests()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var options = new RateLimitOptions { MaxRequests = 2, WindowDuration = TimeSpan.FromMinutes(1) };
        var middleware = new RateLimitingMiddleware(next, _logger.Object, options);

        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/v1/test";
        context1.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        await middleware.InvokeAsync(context1);
        await middleware.InvokeAsync(context1);

        // Third request should be rate limited
        var context3 = new DefaultHttpContext();
        context3.Request.Path = "/api/v1/test";
        context3.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        await middleware.InvokeAsync(context3);

        context3.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task InvokeAsync_HealthPath_ShouldSkipRateLimiting()
    {
        var callCount = 0;
        RequestDelegate next = _ => { callCount++; return Task.CompletedTask; };
        var options = new RateLimitOptions { MaxRequests = 1 };
        var middleware = new RateLimitingMiddleware(next, _logger.Object, options);

        // Make multiple health requests (would exceed 1 request limit)
        for (int i = 0; i < 5; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/health";
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.2");
            await middleware.InvokeAsync(context);
        }

        callCount.Should().Be(5);
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddRateLimitHeaders()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var headersOptions = new HttpHeadersOptions();
        var middleware = new RateLimitingMiddleware(next, _logger.Object, headersOptions: headersOptions);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/test";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.3");

        await middleware.InvokeAsync(context);

        context.Response.Headers[headersOptions.RateLimitLimit].ToString().Should().NotBeEmpty();
        context.Response.Headers[headersOptions.RateLimitRemaining].ToString().Should().NotBeEmpty();
        context.Response.Headers[headersOptions.RateLimitReset].ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_XForwardedFor_ShouldUseFirstIp()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var options = new RateLimitOptions { MaxRequests = 1 };
        var middleware = new RateLimitingMiddleware(next, _logger.Object, options);

        // First request with X-Forwarded-For
        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/test";
        context1.Request.Headers["X-Forwarded-For"] = "192.168.1.1, 10.0.0.1";
        await middleware.InvokeAsync(context1);

        // Second request with same forwarded IP should be rate limited
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/test";
        context2.Request.Headers["X-Forwarded-For"] = "192.168.1.1, 10.0.0.2";
        await middleware.InvokeAsync(context2);

        context2.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task InvokeAsync_DifferentClients_ShouldTrackSeparately()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var options = new RateLimitOptions { MaxRequests = 1 };
        var middleware = new RateLimitingMiddleware(next, _logger.Object, options);

        // Request from client 1
        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/test";
        context1.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.10");
        await middleware.InvokeAsync(context1);

        // Request from client 2 should NOT be rate limited
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/test";
        context2.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.11");
        await middleware.InvokeAsync(context2);

        context2.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task InvokeAsync_OverLimit_ShouldIncludeRetryAfterHeader()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var options = new RateLimitOptions { MaxRequests = 1, WindowDuration = TimeSpan.FromMinutes(5) };
        var middleware = new RateLimitingMiddleware(next, _logger.Object, options);

        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/test";
        context1.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.20");
        await middleware.InvokeAsync(context1);

        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/test";
        context2.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.20");
        await middleware.InvokeAsync(context2);

        context2.Response.StatusCode.Should().Be(429);
        context2.Response.Headers.RetryAfter.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void RateLimitOptions_ShouldHaveDefaults()
    {
        var options = new RateLimitOptions();

        options.MaxRequests.Should().Be(100);
        options.WindowDuration.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void HttpHeadersOptions_ShouldHaveDefaults()
    {
        var options = new HttpHeadersOptions();

        options.RateLimitLimit.Should().NotBeNullOrEmpty();
        options.RateLimitRemaining.Should().NotBeNullOrEmpty();
        options.RateLimitReset.Should().NotBeNullOrEmpty();
    }
}
