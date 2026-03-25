using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;

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
    public async Task InvokeAsync_SwaggerPath_WithoutApiKey_ShouldBypass()
    {
        // Swagger is not an /api/* route, so auth is bypassed
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
    public async Task InvokeAsync_StaticAsset_ShouldBypass()
    {
        // Static files (/assets/*.js) are not /api/* paths, so auth is bypassed
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/assets/index-BL6didGD.js";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RootPath_ShouldBypass()
    {
        // Root path (/) serves index.html, must bypass auth
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";

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

    // ── Placeholder Key Rejection ────────────────────────────────────

    [Theory]
    [InlineData("REPLACED_BY_KEYVAULT_servicehub_api_key_admin")]
    [InlineData("SET_VIA_SOMETHING")]
    [InlineData("CHANGE_THIS_IN_PRODUCTION")]
    public async Task InvokeAsync_PlaceholderApiKey_ShouldReject(string placeholderKey)
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: [placeholderKey]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = placeholderKey;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // Placeholder keys should be rejected (403 = not in lookup)
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task LoadApiKeys_ScopedKeyWithPlaceholder_ShouldSkip()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Security:Authentication:Enabled"] = "true",
            ["Security:Authentication:ScopedApiKeys:0:Key"] = "REPLACED_BY_KEYVAULT_servicehub_api_key_admin",
            ["Security:Authentication:ScopedApiKeys:0:Description"] = "Admin key",
            ["Security:Authentication:ScopedApiKeys:1:Key"] = "real-valid-key-here",
            ["Security:Authentication:ScopedApiKeys:1:Description"] = "Real key"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        // Real key should work
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "real-valid-key-here";
        await middleware.InvokeAsync(context);
        nextCalled.Should().BeTrue();

        // Placeholder key should fail
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/v1/namespaces";
        context2.Request.Headers["X-API-KEY"] = "REPLACED_BY_KEYVAULT_servicehub_api_key_admin";
        context2.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context2);
        context2.Response.StatusCode.Should().Be(403);
    }

    // ── SPA Token Authentication ─────────────────────────────────────

    private static SpaTokenProvider CreateSpaTokenProvider(bool enabled = true)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Security:SpaToken:Enabled"] = enabled.ToString()
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var logger = new Mock<ILogger<SpaTokenProvider>>();
        return new SpaTokenProvider(config, logger.Object);
    }

    [Fact]
    public async Task InvokeAsync_ValidSpaToken_ShouldCallNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-SPA-Token"] = spaTokenProvider.GenerateToken();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["Authenticated"].Should().Be(true);
        context.Items["AuthMethod"].Should().Be("SpaToken");
    }

    [Fact]
    public async Task InvokeAsync_InvalidSpaToken_NoApiKey_ShouldReturn401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-SPA-Token"] = "invalid-token";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_InvalidSpaToken_WithValidApiKey_ShouldFallThroughToApiKey()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-SPA-Token"] = "invalid-token";
        context.Request.Headers["X-API-KEY"] = "test-key-12345";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["AuthMethod"].Should().Be("ApiKey");
    }

    [Fact]
    public async Task InvokeAsync_SpaTokenDisabled_ShouldRequireApiKey()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: false);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-SPA-Token"] = "something";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // Should fall through to API key check and fail (no API key provided)
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_InternalSpaTokenPath_ShouldBypass()
    {
        // /internal/spa-token is not an /api/* route, so auth is bypassed
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/internal/spa-token";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
