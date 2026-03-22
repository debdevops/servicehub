using System.IO.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private readonly Mock<ILogger<SecurityHeadersMiddleware>> _logger;

    public SecurityHeadersMiddlewareTests()
    {
        _logger = new Mock<ILogger<SecurityHeadersMiddleware>>();
    }

    private SecurityHeadersMiddleware CreateMiddleware(
        RequestDelegate next,
        SecurityHeadersOptions? options = null,
        bool isProduction = false)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(isProduction ? "Production" : "Development");

        var opts = Options.Create(options ?? new SecurityHeadersOptions());
        return new SecurityHeadersMiddleware(next, _logger.Object, env.Object, opts);
    }

    /// <summary>
    /// Creates an HttpContext with a response feature that properly fires OnStarting callbacks.
    /// </summary>
    private static DefaultHttpContext CreateTestContext(bool isHttps = false)
    {
        var features = new FeatureCollection();
        var responseFeature = new TestResponseFeature();
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(responseFeature);
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());

        var context = new DefaultHttpContext(features);
        if (isHttps) context.Request.Scheme = "https";
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenDisabled_ShouldNotAddHeaders()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var options = new SecurityHeadersOptions { Enabled = false };
        var middleware = CreateMiddleware(next, options);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddXContentTypeOptions()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddXFrameOptions()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddReferrerPolicy()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddXssProtection()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
    }

    [Fact]
    public async Task InvokeAsync_InDevelopment_ShouldUseDevelopmentCsp()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, isProduction: false);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["Content-Security-Policy"].ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_InProduction_ShouldUseProductionCsp()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, isProduction: true);

        var context = CreateTestContext(isHttps: true);
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["Content-Security-Policy"].ToString()
            .Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddApiVersionHeader()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["X-API-Version"].ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldRemoveServerHeader()
    {
        RequestDelegate next = ctx =>
        {
            ctx.Response.Headers["Server"] = "Kestrel";
            return Task.CompletedTask;
        };
        var middleware = CreateMiddleware(next);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers.Should().NotContainKey("Server");
    }

    [Fact]
    public async Task InvokeAsync_Production_Https_ShouldAddHstsHeader()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, isProduction: true);

        var context = CreateTestContext(isHttps: true);
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers["Strict-Transport-Security"].ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_Development_ShouldNotAddHsts()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, isProduction: false);

        var context = CreateTestContext();
        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public void SecurityHeadersOptions_ShouldHaveDefaults()
    {
        var options = new SecurityHeadersOptions();

        options.ContentTypeOptions.Should().Be("nosniff");
        options.FrameOptions.Should().Be("DENY");
        options.ReferrerPolicy.Should().Be("strict-origin-when-cross-origin");
        options.XssProtection.Should().Be("1; mode=block");
        options.ContentSecurityPolicyProduction.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// A test double that implements both IHttpResponseFeature and IHttpResponseBodyFeature,
    /// properly firing OnStarting callbacks when StartAsync is called.
    /// </summary>
    private sealed class TestResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();

        // IHttpResponseFeature
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
            => _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        // IHttpResponseBodyFeature
        public Stream Stream => Body;
        public PipeWriter Writer => PipeWriter.Create(Body);

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (!HasStarted)
            {
                HasStarted = true;
                // Fire OnStarting callbacks in reverse order (LIFO), matching ASP.NET Core behavior
                for (var i = _onStarting.Count - 1; i >= 0; i--)
                {
                    await _onStarting[i].Callback(_onStarting[i].State);
                }
            }
        }

        public Task CompleteAsync() => Task.CompletedTask;
        public void DisableBuffering() { }
        public Task SendFileAsync(string path, long offset, long? count,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
