using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ServiceHub.Api.Middleware;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Api.Middleware;

public sealed class OidcBearerAuthenticationMiddlewareTests
{
    private const string Authority = "https://idp.example.com";
    private const string Audience = "servicehub-api";

    // Fixed test-only RSA key — never used outside this test file. A fresh key is generated
    // once per test run rather than per test, purely to keep the tests fast.
    private static readonly RsaSecurityKey SigningKey = new(System.Security.Cryptography.RSA.Create(2048))
    {
        KeyId = "test-key-1",
    };

    private static FakeConfigurationManager BuildFakeConfigurationManager()
    {
        var config = new OpenIdConnectConfiguration { Issuer = Authority };
        config.SigningKeys.Add(SigningKey);
        return new FakeConfigurationManager(config);
    }

    private static string BuildToken(
        string? issuer = Authority,
        string? audience = Audience,
        string? subject = "user-123",
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        string? scope = null,
        string? namespaces = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }
        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }
        if (namespaces is not null)
        {
            claims.Add(new Claim("namespaces", namespaces));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256));

        return handler.WriteToken(token);
    }

    private static OidcBearerAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next,
        bool enabled = true,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager = null)
    {
        var options = Options.Create(new OidcOptions
        {
            Enabled = enabled,
            Authority = Authority,
            Audience = Audience,
        });

        return new OidcBearerAuthenticationMiddleware(
            next,
            NullLogger<OidcBearerAuthenticationMiddleware>.Instance,
            options,
            configurationManager ?? BuildFakeConfigurationManager());
    }

    // ── Disabled / no-op paths ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThroughWithoutSettingOwnerId()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next, enabled: false);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken()}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_NoAuthorizationHeader_PassesThrough()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_NonBearerAuthorizationHeader_PassesThrough()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    // ── Valid token ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ValidToken_SetsOwnerIdFromSubjectClaim()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(subject: "user-abc")}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["OwnerId"].Should().Be("oidc:user-abc");
        context.Items["Authenticated"].Should().Be(true);
        context.Items["AuthMethod"].Should().Be("Oidc");
    }

    // ── Optional 'scope' claim ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ValidTokenWithoutScopeClaim_DoesNotSetOidcScopes()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken()}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OidcScopes");
    }

    [Fact]
    public async Task InvokeAsync_ValidTokenWithScopeClaim_SetsOidcScopes()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(scope: "dlq:read dlq:write")}";

        await middleware.InvokeAsync(context);

        context.Items["OidcScopes"].Should().BeEquivalentTo(new[] { "dlq:read", "dlq:write" });
    }

    [Fact]
    public async Task InvokeAsync_ScopeClaimContainsRoleName_ExpandsToScopes()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(scope: "Viewer")}";

        await middleware.InvokeAsync(context);

        var scopes = (string[])context.Items["OidcScopes"]!;
        scopes.Should().Contain("dlq:read");
        scopes.Should().Contain("namespaces:read");
    }

    // ── Optional 'namespaces' claim ──────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ValidTokenWithoutNamespacesClaim_DoesNotSetAllowedNamespaceIds()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken()}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("AllowedNamespaceIds");
    }

    [Fact]
    public async Task InvokeAsync_ValidTokenWithNamespacesClaim_SetsAllowedNamespaceIds()
    {
        var namespaceId1 = Guid.NewGuid();
        var namespaceId2 = Guid.NewGuid();
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization =
            $"Bearer {BuildToken(namespaces: $"{namespaceId1} {namespaceId2}")}";

        await middleware.InvokeAsync(context);

        var allowedNamespaceIds = context.Items["AllowedNamespaceIds"].Should()
            .BeAssignableTo<IReadOnlySet<Guid>>().Subject;
        allowedNamespaceIds.Should().BeEquivalentTo([namespaceId1, namespaceId2]);
    }

    // ── Invalid tokens fall through gracefully (no hard 401 here) ──────────────

    [Theory]
    [InlineData("wrong-issuer")]
    public async Task InvokeAsync_WrongIssuer_FallsThroughWithoutOwnerId(string _)
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(issuer: "https://not-the-idp.example.com")}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_WrongAudience_FallsThroughWithoutOwnerId()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(audience: "some-other-app")}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_ExpiredToken_FallsThroughWithoutOwnerId()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization =
            $"Bearer {BuildToken(expires: DateTime.UtcNow.AddHours(-1))}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_BadSignature_FallsThroughWithoutOwnerId()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var attackerKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "test-key-1" };
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(signingKey: attackerKey)}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_MalformedToken_FallsThroughWithoutThrowing()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer not-a-real-jwt";

        var act = async () => await middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync();
        context.Items.Should().NotContainKey("OwnerId");
    }

    [Fact]
    public async Task InvokeAsync_NoSubjectClaim_FallsThroughWithoutOwnerId()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(subject: null)}";

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey("OwnerId");
    }

    // ── Upstream identity already present ──────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_EasyAuthAlreadySetOwnerId_DoesNotOverwrite()
    {
        // Both EasyAuth (Azure header) and a Bearer token could theoretically be present on
        // the same request (e.g. a proxy adds both). Whichever middleware runs first wins —
        // this one must never clobber an identity an earlier middleware already established.
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {BuildToken(subject: "user-abc")}";
        context.Items["OwnerId"] = "entra:existing-user";
        context.Items["AuthMethod"] = "EasyAuth";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["OwnerId"].Should().Be("entra:existing-user");
        context.Items["AuthMethod"].Should().Be("EasyAuth");
    }

    /// <summary>Test double standing in for the real discovery-document-backed manager.</summary>
    private sealed class FakeConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration;

        public FakeConfigurationManager(OpenIdConnectConfiguration configuration) => _configuration = configuration;

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(_configuration);

        public void RequestRefresh()
        {
        }
    }
}
