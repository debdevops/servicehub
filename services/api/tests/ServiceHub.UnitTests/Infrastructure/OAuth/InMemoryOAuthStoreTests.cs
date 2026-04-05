using FluentAssertions;
using ServiceHub.Infrastructure.OAuth;

namespace ServiceHub.UnitTests.Infrastructure.OAuth;

public sealed class InMemoryOAuthStoreTests
{
    private readonly InMemoryOAuthStore _store = new();

    private static OAuthSession MakeSession(string sessionId, DateTimeOffset? createdAt = null) => new()
    {
        SessionId = sessionId,
        UserPrincipalName = "alice@contoso.com",
        TenantId = "tenant-001",
        ArmAccessToken = "arm-token",
        ArmTokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
        RefreshToken = "refresh-token",
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    // ── Session CRUD ──────────────────────────────────────────────────────────

    [Fact]
    public void StoreSession_ThenGetSession_ReturnsSameSession()
    {
        var session = MakeSession("s1");
        _store.StoreSession(session);

        var result = _store.GetSession("s1");

        result.Should().NotBeNull();
        result!.SessionId.Should().Be("s1");
        result.UserPrincipalName.Should().Be("alice@contoso.com");
    }

    [Fact]
    public void GetSession_ReturnsNull_WhenNotFound()
    {
        var result = _store.GetSession("does-not-exist");
        result.Should().BeNull();
    }

    [Fact]
    public void GetSession_ReturnsNull_AndRemoves_WhenExpired()
    {
        var expired = MakeSession("s-expired", DateTimeOffset.UtcNow.AddHours(-9));
        _store.StoreSession(expired);

        var result = _store.GetSession("s-expired");

        result.Should().BeNull();
        // Second get should also be null (entry cleaned up)
        _store.GetSession("s-expired").Should().BeNull();
    }

    [Fact]
    public void RemoveSession_RemovesExistingSession()
    {
        var session = MakeSession("s-to-remove");
        _store.StoreSession(session);

        _store.RemoveSession("s-to-remove");

        _store.GetSession("s-to-remove").Should().BeNull();
    }

    [Fact]
    public void RemoveSession_DoesNotThrow_WhenSessionNotFound()
    {
        var act = () => _store.RemoveSession("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void StoreSession_Overwrites_ExistingEntry()
    {
        var s1 = MakeSession("s-overwrite");
        _store.StoreSession(s1);

        var s2 = MakeSession("s-overwrite");
        s2.ArmAccessToken = "updated-arm-token";
        _store.StoreSession(s2);

        var result = _store.GetSession("s-overwrite");
        result.Should().NotBeNull();
        result!.ArmAccessToken.Should().Be("updated-arm-token");
    }

    // ── PKCE state ────────────────────────────────────────────────────────────

    [Fact]
    public void StorePkceState_ThenGetAndConsume_ReturnsVerifier()
    {
        _store.StorePkceState("state-abc", "verifier-xyz");

        var verifier = _store.GetAndConsumePkceVerifier("state-abc");

        verifier.Should().Be("verifier-xyz");
    }

    [Fact]
    public void GetAndConsumePkceVerifier_IsOneTimeUse()
    {
        _store.StorePkceState("state-once", "verifier-once");

        _store.GetAndConsumePkceVerifier("state-once");
        var second = _store.GetAndConsumePkceVerifier("state-once");

        second.Should().BeNull();
    }

    [Fact]
    public void GetAndConsumePkceVerifier_ReturnsNull_WhenStateNotFound()
    {
        var result = _store.GetAndConsumePkceVerifier("nonexistent-state");
        result.Should().BeNull();
    }

    // ── PurgeExpired ──────────────────────────────────────────────────────────

    [Fact]
    public void PurgeExpired_RemovesExpiredSessions()
    {
        _store.StoreSession(MakeSession("fresh", DateTimeOffset.UtcNow));
        _store.StoreSession(MakeSession("stale", DateTimeOffset.UtcNow.AddHours(-9)));

        _store.PurgeExpired();

        _store.GetSession("fresh").Should().NotBeNull();
        _store.GetSession("stale").Should().BeNull();
    }

    [Fact]
    public void PurgeExpired_DoesNotRemove_ValidPkceStates()
    {
        _store.StorePkceState("valid-state", "code-verifier");

        _store.PurgeExpired(); // loop runs with 1 entry, if-branch is NOT taken

        // Verifier should still be consumable (not purged)
        _store.GetAndConsumePkceVerifier("valid-state").Should().Be("code-verifier");
    }
}
