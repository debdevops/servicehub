using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Configuration;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class AzureAuthControllerTests
{
    private readonly Mock<IOAuthService> _oauthService = new();
    private readonly OAuthOptions _opts = new();
    private AzureAuthController CreateController(OAuthOptions? opts = null)
    {
        var controller = new AzureAuthController(
            _oauthService.Object,
            Options.Create(opts ?? _opts),
            NullLogger<AzureAuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return controller;
    }

    // ── Constructor null guards ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NullOAuthService_Throws()
    {
        var act = () => new AzureAuthController(
            null!,
            Options.Create(_opts),
            NullLogger<AzureAuthController>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("oauthService");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new AzureAuthController(
            _oauthService.Object,
            Options.Create<OAuthOptions>(null!),
            NullLogger<AzureAuthController>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AzureAuthController(
            _oauthService.Object,
            Options.Create(_opts),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetStatus ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_NoCookie_ReturnsNotSignedIn()
    {
        _oauthService.Setup(s => s.IsConfigured).Returns(false);
        var controller = CreateController();

        var result = controller.GetStatus() as OkObjectResult;

        result.Should().NotBeNull();
        var response = result!.Value as AzureAuthStatusResponse;
        response.Should().NotBeNull();
        response!.IsSignedIn.Should().BeFalse();
        response.UserPrincipalName.Should().BeNull();
    }

    [Fact]
    public void GetStatus_WithCookie_SessionFound_ReturnsSignedIn()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(7);
        var sessionInfo = new OAuthSessionInfo("sid", "alice@contoso.com", "tenant-001", expiry);
        _oauthService.Setup(s => s.IsConfigured).Returns(true);
        _oauthService.Setup(s => s.GetSessionInfo("sid")).Returns(sessionInfo);

        var controller = CreateController();
        controller.HttpContext.Request.Headers.Cookie = "servicehub_oauth_session=sid";

        var result = controller.GetStatus() as OkObjectResult;
        var response = result!.Value as AzureAuthStatusResponse;

        response!.IsSignedIn.Should().BeTrue();
        response.UserPrincipalName.Should().Be("alice@contoso.com");
        response.TenantId.Should().Be("tenant-001");
        response.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public void GetStatus_IsConfigured_ReflectsOAuthOptions()
    {
        _oauthService.Setup(s => s.IsConfigured).Returns(true);
        var controller = CreateController();

        var result = (controller.GetStatus() as OkObjectResult)!.Value as AzureAuthStatusResponse;
        result!.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void GetStatus_WithCookie_SessionNotFound_ReturnsNotSignedIn()
    {
        _oauthService.Setup(s => s.IsConfigured).Returns(true);
        _oauthService.Setup(s => s.GetSessionInfo(It.IsAny<string>())).Returns((OAuthSessionInfo?)null);

        var controller = CreateController();
        controller.HttpContext.Request.Headers.Cookie = "servicehub_oauth_session=unknown";

        var result = (controller.GetStatus() as OkObjectResult)!.Value as AzureAuthStatusResponse;
        result!.IsSignedIn.Should().BeFalse();
    }

    // ── GetSignInUrl ──────────────────────────────────────────────────────────

    [Fact]
    public void GetSignInUrl_NotConfigured_Returns503()
    {
        _oauthService.Setup(s => s.IsConfigured).Returns(false);
        var controller = CreateController();

        var result = controller.GetSignInUrl();

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void GetSignInUrl_Configured_Returns200WithUrl()
    {
        _oauthService.Setup(s => s.IsConfigured).Returns(true);
        _oauthService.Setup(s => s.GenerateSignInUrl())
            .Returns(("https://login.microsoftonline.com/authorize?...", "state-abc"));
        var controller = CreateController();

        var result = (controller.GetSignInUrl() as OkObjectResult)!.Value as AzureSignInUrlResponse;

        result.Should().NotBeNull();
        result!.AuthorizationUrl.Should().StartWith("https://login.microsoftonline.com");
    }

    // ── Callback ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_AzureError_RedirectsWithErrorMsg()
    {
        var opts = new OAuthOptions { FrontendBaseUrl = "http://localhost:3000" };
        var controller = CreateController(opts);

        var result = await controller.Callback(null, null, "access_denied", "User cancelled.", default);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("auth=error");
    }

    [Fact]
    public async Task Callback_MissingCodeOrState_RedirectsWithError()
    {
        var opts = new OAuthOptions { FrontendBaseUrl = "http://localhost:3000" };
        var controller = CreateController(opts);

        var result = await controller.Callback(null, null, null, null, default);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("auth=error");
    }

    [Fact]
    public async Task Callback_ExchangeCodeFails_RedirectsWithErrorMsg()
    {
        var opts = new OAuthOptions { FrontendBaseUrl = "http://localhost:3000" };
        _oauthService.Setup(s => s.ExchangeCodeAsync("code", "state", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(Error.Validation("INVALID", "Invalid code")));
        var controller = CreateController(opts);

        var result = await controller.Callback("code", "state", null, null, default);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("auth=error");
    }

    [Fact]
    public async Task Callback_Success_SetsCookieAndRedirectsToFrontend()
    {
        var opts = new OAuthOptions { FrontendBaseUrl = "http://localhost:3000" };
        _oauthService.Setup(s => s.ExchangeCodeAsync("code", "state", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("new-session-id"));

        var httpContext = new DefaultHttpContext();
        var controller = new AzureAuthController(
            _oauthService.Object,
            Options.Create(opts),
            NullLogger<AzureAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await controller.Callback("code", "state", null, null, default);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("auth=success");
        redirect.Url.Should().Contain("tab=entra");
    }

    // ── ListNamespaces ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListNamespaces_NoCookie_Returns401()
    {
        var controller = CreateController();

        var result = await controller.ListNamespaces(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ListNamespaces_SessionNotFound_Returns401()
    {
        var error = Error.Validation("SessionNotFound", "Session not found.");
        _oauthService.Setup(s => s.ListNamespacesAsync("sid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<AzureNamespaceInfo>>(error));

        var controller = CreateController();
        controller.HttpContext.Request.Headers.Cookie = "servicehub_oauth_session=sid";

        var result = await controller.ListNamespaces(default);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ListNamespaces_Success_ReturnsOkWithNamespaces()
    {
        IReadOnlyList<AzureNamespaceInfo> namespaces =
        [
            new AzureNamespaceInfo("mybus", "mybus.servicebus.windows.net", "sub-id", "rg", "eastus", "Standard"),
        ];
        _oauthService.Setup(s => s.ListNamespacesAsync("sid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(namespaces));

        var controller = CreateController();
        controller.HttpContext.Request.Headers.Cookie = "servicehub_oauth_session=sid";

        var result = await controller.ListNamespaces(default) as OkObjectResult;
        result.Should().NotBeNull();
        var value = result!.Value as IReadOnlyList<AzureNamespaceInfo>;
        value.Should().HaveCount(1);
        value![0].Name.Should().Be("mybus");
    }

    // ── SignOut ───────────────────────────────────────────────────────────────

    [Fact]
    public void SignOut_NoCookie_Returns204()
    {
        var controller = CreateController();

        var result = controller.SignOut();

        result.Should().BeOfType<NoContentResult>();
        _oauthService.Verify(s => s.RevokeSession(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void SignOut_WithCookie_RevokesSessionAndReturns204()
    {
        var controller = CreateController();
        controller.HttpContext.Request.Headers.Cookie = "servicehub_oauth_session=sid";

        var result = controller.SignOut();

        result.Should().BeOfType<NoContentResult>();
        _oauthService.Verify(s => s.RevokeSession("sid"), Times.Once);
    }
}
