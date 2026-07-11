using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api.Middleware;

public class EasyAuthMiddlewareTests
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL-ID";

    private readonly Mock<ILogger<EasyAuthMiddleware>> _logger = new();

    private static IConfiguration CreateConfig(
        bool? enabled = null,
        bool? trustClientPrincipalHeader = null)
    {
        var dict = new Dictionary<string, string?>();
        if (enabled is not null)
            dict["Security:EasyAuth:Enabled"] = enabled.ToString();
        if (trustClientPrincipalHeader is not null)
            dict["Security:EasyAuth:TrustClientPrincipalHeader"] = trustClientPrincipalHeader.ToString();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private EasyAuthMiddleware CreateMiddleware(IConfiguration config, RequestDelegate next)
        => new(next, _logger.Object, config);

    [Fact]
    public async Task InvokeAsync_HeaderPresentButNotBehindAzureEasyAuth_ShouldIgnoreHeader()
    {
        // SECURITY: on non-Azure hosts (AWS ECS, GCP, bare Kestrel) nothing strips this
        // header from inbound traffic, so it must not be trusted even when the config
        // flag is enabled — otherwise any client can mint an arbitrary identity.
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(CreateConfig(enabled: true), next);

        var context = new DefaultHttpContext();
        context.Request.Headers[PrincipalHeader] = "attacker-supplied-oid";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items.Should().NotContainKey("OwnerId");
        context.Items.Should().NotContainKey("Authenticated");
        context.Items.Should().NotContainKey("AuthMethod");
    }

    [Fact]
    public async Task InvokeAsync_ExplicitHeaderTrust_ShouldAuthenticateFromHeader()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(
            CreateConfig(enabled: true, trustClientPrincipalHeader: true), next);

        var context = new DefaultHttpContext();
        context.Request.Headers[PrincipalHeader] = "user-oid-123";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["OwnerId"].Should().Be("entra:user-oid-123");
        context.Items["Authenticated"].Should().Be(true);
        context.Items["AuthMethod"].Should().Be("EasyAuth");
    }

    [Fact]
    public async Task InvokeAsync_BehindAzureEasyAuth_ShouldAuthenticateFromHeader()
    {
        // Azure App Service sets WEBSITE_AUTH_ENABLED=True on the worker when
        // Easy Auth is active — the only environment where the header is unforgeable.
        var previousValue = Environment.GetEnvironmentVariable("WEBSITE_AUTH_ENABLED");
        Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENABLED", "True");
        try
        {
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = CreateMiddleware(CreateConfig(enabled: true), next);

            var context = new DefaultHttpContext();
            context.Request.Headers[PrincipalHeader] = "user-oid-456";

            await middleware.InvokeAsync(context);

            context.Items["OwnerId"].Should().Be("entra:user-oid-456");
            context.Items["AuthMethod"].Should().Be("EasyAuth");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENABLED", previousValue);
        }
    }

    [Fact]
    public async Task InvokeAsync_DisabledInConfig_ShouldIgnoreHeaderEvenWithTrust()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(
            CreateConfig(enabled: false, trustClientPrincipalHeader: true), next);

        var context = new DefaultHttpContext();
        context.Request.Headers[PrincipalHeader] = "user-oid-789";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_NoHeader_ShouldPassThrough()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(
            CreateConfig(enabled: true, trustClientPrincipalHeader: true), next);

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items.Should().NotContainKey("OwnerId");
    }
}
