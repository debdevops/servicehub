using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;

namespace ServiceHub.UnitTests.Api.Filters;

public sealed class ScopeAuthorizationFilterTests
{
    private static ScopeAuthorizationFilter CreateFilter(bool authEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:Enabled"] = authEnabled.ToString()
            })
            .Build();

        return new ScopeAuthorizationFilter(
            NullLogger<ScopeAuthorizationFilter>.Instance,
            config);
    }

    private static AuthorizationFilterContext CreateContext(
        ApiKeyConfiguration? keyConfig = null,
        List<object>? endpointMetadata = null)
    {
        var httpContext = new DefaultHttpContext();
        if (keyConfig != null)
        {
            httpContext.Items["ApiKeyConfig"] = keyConfig;
        }
        httpContext.Items["CorrelationId"] = "test-123";

        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = endpointMetadata ?? new List<object>()
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public async Task AuthDisabled_SkipsAuthorization()
    {
        var filter = CreateFilter(authEnabled: false);
        var context = CreateContext(endpointMetadata: new List<object>
        {
            new RequireScopeAttribute("admin")
        });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task NoRequiredScope_AllowsAccess()
    {
        var filter = CreateFilter();
        var context = CreateContext(); // No RequireScopeAttribute

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RequiredScope_NoApiKey_Returns401()
    {
        var filter = CreateFilter();
        var context = CreateContext(
            keyConfig: null,
            endpointMetadata: new List<object> { new RequireScopeAttribute("messages:peek") });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().NotBeNull();
        var jsonResult = context.Result as JsonResult;
        jsonResult.Should().NotBeNull();
        jsonResult!.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task RequiredScope_KeyWithoutScope_Returns403()
    {
        var keyConfig = new ApiKeyConfiguration
        {
            Key = "test-key-12345",
            Scopes = new[] { "messages:peek" }
        };

        var filter = CreateFilter();
        var context = CreateContext(
            keyConfig: keyConfig,
            endpointMetadata: new List<object> { new RequireScopeAttribute("dlq:write") });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().NotBeNull();
        var jsonResult = context.Result as JsonResult;
        jsonResult.Should().NotBeNull();
        jsonResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task RequiredScope_KeyWithScope_AllowsAccess()
    {
        var keyConfig = new ApiKeyConfiguration
        {
            Key = "test-key-12345",
            Scopes = new[] { "messages:peek" }
        };

        var filter = CreateFilter();
        var context = CreateContext(
            keyConfig: keyConfig,
            endpointMetadata: new List<object> { new RequireScopeAttribute("messages:peek") });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RequiredScope_AdminKey_AllowsAllScopes()
    {
        // Keys with no scopes have admin access
        var keyConfig = new ApiKeyConfiguration
        {
            Key = "admin-key-12345",
            Scopes = null
        };

        var filter = CreateFilter();
        var context = CreateContext(
            keyConfig: keyConfig,
            endpointMetadata: new List<object> { new RequireScopeAttribute("dlq:write") });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RequiredScope_EmptyScopesKey_AllowsAllScopes()
    {
        var keyConfig = new ApiKeyConfiguration
        {
            Key = "admin-key-12345",
            Scopes = Array.Empty<string>()
        };

        var filter = CreateFilter();
        var context = CreateContext(
            keyConfig: keyConfig,
            endpointMetadata: new List<object> { new RequireScopeAttribute("messages:peek") });

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RequiredScope_NonApiKeyObject_Returns401()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["ApiKeyConfig"] = "not-api-key-config"; // wrong type
        httpContext.Items["CorrelationId"] = "test";

        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = new List<object> { new RequireScopeAttribute("admin") }
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
        var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var filter = CreateFilter();
        await filter.OnAuthorizationAsync(context);

        context.Result.Should().NotBeNull();
        (context.Result as JsonResult)!.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}
