using FluentAssertions;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.Models;

public sealed class OAuthSessionInfoTests
{
    [Fact]
    public void IsExpired_ReturnsFalse_WhenExpiresInFuture()
    {
        var info = new OAuthSessionInfo(
            "session-id",
            "alice@contoso.com",
            "tenant-001",
            DateTimeOffset.UtcNow.AddHours(1));

        info.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenExpiresInPast()
    {
        var info = new OAuthSessionInfo(
            "session-id",
            "alice@contoso.com",
            "tenant-001",
            DateTimeOffset.UtcNow.AddSeconds(-1));

        info.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void Properties_AreSetCorrectly()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(8);
        var info = new OAuthSessionInfo("sid", "user@test.com", "t123", expiry);

        info.SessionId.Should().Be("sid");
        info.UserPrincipalName.Should().Be("user@test.com");
        info.TenantId.Should().Be("t123");
        info.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public void Record_Equality_Works()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(4);
        var a = new OAuthSessionInfo("sid", "user@test.com", "t1", expiry);
        var b = new OAuthSessionInfo("sid", "user@test.com", "t1", expiry);

        a.Should().Be(b);
    }
}
