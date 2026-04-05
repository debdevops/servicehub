using Azure.Core;
using FluentAssertions;
using ServiceHub.Infrastructure.OAuth;

namespace ServiceHub.UnitTests.Infrastructure.OAuth;

public sealed class OAuthSessionCredentialTests
{
    private static OAuthSession MakeSession(string? sbToken = null, DateTimeOffset? sbExpiry = null) => new()
    {
        SessionId = "session-001",
        UserPrincipalName = "alice@contoso.com",
        TenantId = "tenant-001",
        ArmAccessToken = "arm-token",
        ArmTokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
        RefreshToken = "refresh-token",
        CreatedAt = DateTimeOffset.UtcNow,
        SbAccessToken = sbToken,
        SbTokenExpiry = sbExpiry ?? DateTimeOffset.MinValue,
    };

    private static Task<(string Token, DateTimeOffset Expiry, string NewRefreshToken)> FakeRefresh(
        OAuthSession _, CancellationToken __)
        => Task.FromResult(("new-sb-token", DateTimeOffset.UtcNow.AddHours(1), "new-refresh"));

    // ── GetTokenAsync: fresh cache hit ────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_ReturnsCachedToken_WhenValid()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);
        var session = MakeSession("cached-sb-token", expiry);
        var credential = new OAuthSessionCredential(session, FakeRefresh);

        var token = await credential.GetTokenAsync(new TokenRequestContext(), CancellationToken.None);

        token.Token.Should().Be("cached-sb-token");
        token.ExpiresOn.Should().Be(expiry);
    }

    // ── GetTokenAsync: expired token triggers refresh ─────────────────────────

    [Fact]
    public async Task GetTokenAsync_RefreshesToken_WhenExpired()
    {
        // Token expired 1 hour ago
        var session = MakeSession("old-sb-token", DateTimeOffset.UtcNow.AddHours(-1));
        var credential = new OAuthSessionCredential(session, FakeRefresh);

        var token = await credential.GetTokenAsync(new TokenRequestContext(), CancellationToken.None);

        token.Token.Should().Be("new-sb-token");
        session.SbAccessToken.Should().Be("new-sb-token");
        session.RefreshToken.Should().Be("new-refresh");
    }

    // ── GetTokenAsync: no cached token forces refresh ─────────────────────────

    [Fact]
    public async Task GetTokenAsync_RefreshesToken_WhenNoSbTokenCached()
    {
        var session = MakeSession(sbToken: null);
        var credential = new OAuthSessionCredential(session, FakeRefresh);

        var token = await credential.GetTokenAsync(new TokenRequestContext(), CancellationToken.None);

        token.Token.Should().Be("new-sb-token");
    }

    // ── GetToken (sync) ───────────────────────────────────────────────────────

    [Fact]
    public void GetToken_SynchronouslyReturnsCachedToken()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);
        var session = MakeSession("cached-sb-token", expiry);
        var credential = new OAuthSessionCredential(session, FakeRefresh);

        var token = credential.GetToken(new TokenRequestContext(), CancellationToken.None);

        token.Token.Should().Be("cached-sb-token");
    }

    // ── Refresh with no new refresh token ─────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_DoesNotOverwriteRefreshToken_WhenNewOneIsEmpty()
    {
        var session = MakeSession("old", DateTimeOffset.UtcNow.AddHours(-1));
        session.RefreshToken = "original-refresh";

        Task<(string, DateTimeOffset, string)> RefreshWithEmptyToken(OAuthSession _, CancellationToken __)
            => Task.FromResult(("new-token", DateTimeOffset.UtcNow.AddHours(1), string.Empty));

        var credential = new OAuthSessionCredential(session, RefreshWithEmptyToken);
        await credential.GetTokenAsync(new TokenRequestContext(), CancellationToken.None);

        session.RefreshToken.Should().Be("original-refresh");
    }
}
