using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public class ApiKeyAuthenticationMiddlewareTests
{
    private readonly Mock<ILogger<ApiKeyAuthenticationMiddleware>> _logger;

    public ApiKeyAuthenticationMiddlewareTests()
    {
        _logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
    }

    private static IConfiguration CreateConfig(bool enabled = true, string[]? apiKeys = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Security:Authentication:Enabled"] = enabled.ToString()
        };

        if (apiKeys != null)
        {
            for (int i = 0; i < apiKeys.Length; i++)
            {
                dict[$"Security:Authentication:ApiKeys:{i}"] = apiKeys[i];
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public async Task InvokeAsync_WhenDisabled_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: false);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HealthPath_ShouldBypass()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HealthReadyPath_ShouldBypass()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SwaggerPath_ShouldBypass()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/swagger/index.html";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MissingApiKey_ShouldReturn401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_EmptyApiKey_ShouldReturn401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_InvalidApiKey_ShouldReturn403()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "wrong-key";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_ValidApiKey_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "test-key-12345";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["Authenticated"].Should().Be(true);
        context.Items["AuthMethod"].Should().Be("ApiKey");
    }

    [Fact]
    public async Task InvokeAsync_ValidApiKey_ShouldStoreApiKeyConfig()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "test-key-12345";

        await middleware.InvokeAsync(context);

        context.Items["ApiKeyConfig"].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_NoKeysConfigured_ShouldReturn401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "any-key";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_ApiV1HealthLive_ShouldBypass()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/health/live";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
