using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;
using Microsoft.Extensions.Configuration;

namespace ServiceHub.UnitTests.Api.Middleware;

public class SpaTokenInjectionMiddlewareTests
{
    private static SpaTokenProvider CreateEnabledProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:SpaToken:Enabled"] = "true",
                ["Security:EncryptionKey"] = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            })
            .Build();
        return new SpaTokenProvider(config, NullLogger<SpaTokenProvider>.Instance);
    }

    private static SpaTokenProvider CreateDisabledProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:SpaToken:Enabled"] = "false"
            })
            .Build();
        return new SpaTokenProvider(config, NullLogger<SpaTokenProvider>.Instance);
    }

    [Fact]
    public async Task ApiRequest_PassesThrough_WhenSpaEnabled()
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task HealthRequest_PassesThrough()
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task StaticAsset_PassesThrough()
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/assets/main.js";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("/assets/style.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/fonts/inter.woff2")]
    [InlineData("/fonts/inter.woff")]
    [InlineData("/images/logo.png")]
    [InlineData("/images/logo.svg")]
    [InlineData("/assets/main.js.map")]
    [InlineData("/manifest.json")]
    public async Task StaticAssetExtensions_PassThrough(string path)
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Disabled_PassesThrough_WithoutInjection()
    {
        var provider = CreateDisabledProvider();
        var html = "<html><head></head><body>hello</body></html>";
        var middleware = new SpaTokenInjectionMiddleware(
            next: async ctx =>
            {
                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync(html);
            },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Be(html);
    }

    [Fact]
    public async Task HtmlResponse_InjectsMetaTag()
    {
        var provider = CreateEnabledProvider();
        var html = "<html><head></head><body>hello</body></html>";
        var middleware = new SpaTokenInjectionMiddleware(
            next: async ctx =>
            {
                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync(html);
            },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("spa-token");
        body.Should().Contain("<meta name=\"spa-token\"");
    }

    [Fact]
    public async Task NonHtmlResponse_PassesThrough()
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Be("{\"ok\":true}");
        body.Should().NotContain("spa-token");
    }

    [Fact]
    public async Task NonOkStatus_PassesThrough()
    {
        var provider = CreateEnabledProvider();
        var middleware = new SpaTokenInjectionMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/html";
                return Task.CompletedTask;
            },
            tokenProvider: provider,
            logger: NullLogger<SpaTokenInjectionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }
}
