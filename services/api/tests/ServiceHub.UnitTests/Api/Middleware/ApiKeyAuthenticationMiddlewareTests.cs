using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Api.Authorization;
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
    public async Task InvokeAsync_InvalidApiKey_ShouldReturn401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "wrong-key";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
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

        context.Response.StatusCode.Should().Be(401);
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

        // Placeholder keys should be rejected (401 = credential not recognised)
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoadApiKeys_ScopedKeyWithRoleName_ExpandsToScopesAtLoadTime()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Security:Authentication:Enabled"] = "true",
            ["Security:Authentication:ScopedApiKeys:0:Key"] = "viewer-key",
            ["Security:Authentication:ScopedApiKeys:0:Scopes:0"] = "Viewer",
            ["Security:Authentication:ScopedApiKeys:0:Description"] = "Viewer role key",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "viewer-key";

        await middleware.InvokeAsync(context);

        var keyConfig = (ApiKeyConfiguration)context.Items["ApiKeyConfig"]!;
        keyConfig.Scopes.Should().Contain(ApiKeyScopes.DlqRead);
        keyConfig.Scopes.Should().Contain(ApiKeyScopes.NamespacesRead);
        keyConfig.Scopes.Should().NotContain(ApiKeyScopes.MessagesSend);
    }

    [Fact]
    public async Task LoadApiKeys_ScopedKeyWithNamespacesAndRoleName_CarriesNamespacesThroughScopeExpansion()
    {
        // Regression test: LoadApiKeys reconstructs a new ApiKeyConfiguration when Scopes is
        // non-empty (to pre-expand role names). That reconstruction must also copy Namespaces —
        // otherwise every key with explicit Scopes (i.e. every scoped key) would silently lose
        // its namespace restriction at startup and become unrestricted.
        var namespaceId = Guid.NewGuid();
        var dict = new Dictionary<string, string?>
        {
            ["Security:Authentication:Enabled"] = "true",
            ["Security:Authentication:ScopedApiKeys:0:Key"] = "restricted-viewer-key",
            ["Security:Authentication:ScopedApiKeys:0:Scopes:0"] = "Viewer",
            ["Security:Authentication:ScopedApiKeys:0:Namespaces:0"] = namespaceId.ToString(),
            ["Security:Authentication:ScopedApiKeys:0:Description"] = "Namespace-restricted viewer key",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "restricted-viewer-key";

        await middleware.InvokeAsync(context);

        var keyConfig = (ApiKeyConfiguration)context.Items["ApiKeyConfig"]!;
        keyConfig.Scopes.Should().Contain(ApiKeyScopes.DlqRead);
        keyConfig.Namespaces.Should().ContainSingle().Which.Should().Be(namespaceId.ToString());

        var allowedNamespaceIds = context.Items["AllowedNamespaceIds"].Should()
            .BeAssignableTo<IReadOnlySet<Guid>>().Subject;
        allowedNamespaceIds.Should().ContainSingle().Which.Should().Be(namespaceId);
    }

    [Fact]
    public async Task InvokeAsync_KeyWithoutNamespaces_DoesNotSetAllowedNamespaceIds()
    {
        var config = CreateConfig(enabled: true, apiKeys: ["unrestricted-key"]);
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = "unrestricted-key";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("AllowedNamespaceIds");
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
        context2.Response.StatusCode.Should().Be(401);
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

    // ── EasyAuth Short-Circuit ───────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_EasyAuthAuthenticated_WithValidSpaToken_ShouldKeepEasyAuthOwnerId()
    {
        // EasyAuth + SpaToken both active (production config): the SPA-token branch
        // must NOT overwrite the per-user EasyAuth OwnerId with the shared SPA owner.
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/events/stream";
        context.Request.Headers["X-SPA-Token"] = spaTokenProvider.GenerateToken();
        context.Items["OwnerId"] = "entra:11111111-2222-3333-4444-555555555555";
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "EasyAuth";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["OwnerId"].Should().Be("entra:11111111-2222-3333-4444-555555555555");
        context.Items["AuthMethod"].Should().Be("EasyAuth");
    }

    [Fact]
    public async Task InvokeAsync_EasyAuthAuthenticated_WithoutSpaTokenOrApiKey_ShouldCallNext()
    {
        // EasyAuth-authenticated request with no other credential must pass through,
        // not 401 at the API-key fall-through.
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var spaTokenProvider = CreateSpaTokenProvider(enabled: true);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, spaTokenProvider);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Items["OwnerId"] = "entra:11111111-2222-3333-4444-555555555555";
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "EasyAuth";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
        context.Items["OwnerId"].Should().Be("entra:11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public async Task InvokeAsync_OwnerIdSetButNotEasyAuth_ShouldNotShortCircuit()
    {
        // Only AuthMethod == "EasyAuth" or "Oidc" may skip credential checks; an OwnerId set by
        // anything else must still be authenticated.
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Items["OwnerId"] = "spoofed-owner";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_OidcAuthenticated_WithoutSpaTokenOrApiKey_ShouldCallNext()
    {
        // OIDC-authenticated request (validated by OidcBearerAuthenticationMiddleware
        // upstream) with no other credential must pass through, not 401 at the API-key
        // fall-through — same short-circuit EasyAuth already gets.
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Items["OwnerId"] = "oidc:user-abc";
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "Oidc";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
        context.Items["OwnerId"].Should().Be("oidc:user-abc");
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

    // ── Auth Failure Throttle ─────────────────────────────────────────

    private static AuthFailureThrottle CreateThrottle(int threshold, TimeSpan? window = null) =>
        new(Options.Create(new AuthFailureThrottleOptions
        {
            Threshold = threshold,
            Window = window ?? TimeSpan.FromMinutes(5)
        }));

    private static DefaultHttpContext CreateApiContext(string apiKey)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/namespaces";
        context.Request.Headers["X-API-KEY"] = apiKey;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_RepeatedInvalidApiKey_LocksOutAfterThreshold()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var throttle = CreateThrottle(threshold: 3);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, authFailureThrottle: throttle);

        for (var i = 0; i < 3; i++)
        {
            var context = CreateApiContext("wrong-key");
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().Be(401, $"attempt {i + 1} is under the lockout threshold");
        }

        var lockedOutContext = CreateApiContext("wrong-key");
        await middleware.InvokeAsync(lockedOutContext);

        lockedOutContext.Response.StatusCode.Should().Be(429);
        lockedOutContext.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_LockedOut_RecoversAfterWindow()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var throttle = CreateThrottle(threshold: 2, window: TimeSpan.FromMilliseconds(50));
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, authFailureThrottle: throttle);

        await middleware.InvokeAsync(CreateApiContext("wrong-key"));
        await middleware.InvokeAsync(CreateApiContext("wrong-key"));

        var lockedOutContext = CreateApiContext("wrong-key");
        await middleware.InvokeAsync(lockedOutContext);
        lockedOutContext.Response.StatusCode.Should().Be(429);

        await Task.Delay(100);

        var recoveredContext = CreateApiContext("wrong-key");
        await middleware.InvokeAsync(recoveredContext);
        recoveredContext.Response.StatusCode.Should().Be(401, "the window has expired, so this is back to a plain invalid-key rejection, not a lockout");
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulAuth_ResetsFailureCount()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var config = CreateConfig(enabled: true, apiKeys: ["test-key-12345"]);
        var throttle = CreateThrottle(threshold: 2);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, authFailureThrottle: throttle);

        await middleware.InvokeAsync(CreateApiContext("wrong-key"));

        var successContext = CreateApiContext("test-key-12345");
        await middleware.InvokeAsync(successContext);
        successContext.Response.StatusCode.Should().Be(200, "the valid key request completes normally");

        // Threshold is 2; without the reset this would be failure #2 and lock out.
        var afterSuccessContext = CreateApiContext("wrong-key");
        await middleware.InvokeAsync(afterSuccessContext);
        afterSuccessContext.Response.StatusCode.Should().Be(401, "the successful auth cleared the prior failure, so this is only failure #1 again");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticationDisabled_ThrottleNeverEngagesRegardlessOfFailureVolume()
    {
        var nextCallCount = 0;
        RequestDelegate next = _ => { nextCallCount++; return Task.CompletedTask; };
        var config = CreateConfig(enabled: false);
        var throttle = CreateThrottle(threshold: 1);
        var middleware = new ApiKeyAuthenticationMiddleware(next, _logger.Object, config, authFailureThrottle: throttle);

        // With auth disabled, every request short-circuits before the throttle is ever consulted —
        // the local dev loop must never lock itself out.
        for (var i = 0; i < 20; i++)
        {
            var context = CreateApiContext("wrong-key");
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().Be(200);
        }

        nextCallCount.Should().Be(20);
    }
}
