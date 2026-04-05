using FluentAssertions;
using ServiceHub.Infrastructure.OAuth;

namespace ServiceHub.UnitTests.Infrastructure.OAuth;

public sealed class OAuthSessionTests
{
    private static OAuthSession CreateSession(DateTimeOffset? createdAt = null) => new()
    {
        SessionId = "session-id-001",
        UserPrincipalName = "alice@contoso.com",
        TenantId = "tenant-001",
        ArmAccessToken = "arm-token",
        ArmTokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
        RefreshToken = "refresh-token",
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ExpiresAt_Is8HoursFromCreatedAt()
    {
        var created = DateTimeOffset.UtcNow.AddHours(-1);
        var session = CreateSession(created);

        session.ExpiresAt.Should().BeCloseTo(created.AddHours(8), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenFresh()
    {
        var session = CreateSession(DateTimeOffset.UtcNow);
        session.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenOlderThan8Hours()
    {
        var session = CreateSession(DateTimeOffset.UtcNow.AddHours(-9));
        session.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void SbAccessToken_IsNullableByDefault()
    {
        var session = CreateSession();
        session.SbAccessToken.Should().BeNull();
    }

    [Fact]
    public void SbTokenExpiry_CanBeSet()
    {
        var session = CreateSession();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(55);
        session.SbTokenExpiry = expiry;
        session.SbTokenExpiry.Should().Be(expiry);
    }

    [Fact]
    public void RefreshToken_CanBeUpdated()
    {
        var session = CreateSession();
        session.RefreshToken = "new-refresh-token";
        session.RefreshToken.Should().Be("new-refresh-token");
    }
}
