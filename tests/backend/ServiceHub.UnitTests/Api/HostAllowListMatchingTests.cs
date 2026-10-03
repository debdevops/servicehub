using FluentAssertions;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api;

/// <summary>How an <c>AllowedHosts</c> entry matches a host name: exactly (any case), or as a <c>*.suffix</c> wildcard that never matches the bare suffix.</summary>
public sealed class HostAllowListMatchingTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LocalHost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("evil.example.com", false)]
    [InlineData("localhost.evil.com", false)]
    [InlineData("xlocalhost", false)]
    [InlineData("", false)]
    public void The_default_allows_only_the_loopback_names(string host, bool expected) =>
        HostAllowListMiddleware.Matches(HostAllowListMiddleware.LoopbackHosts, host).Should().Be(expected);

    [Theory]
    [InlineData("a.contoso.com", true)]
    [InlineData("a.b.contoso.com", true)]
    [InlineData("A.CONTOSO.COM", true)]
    [InlineData("contoso.com", false)]
    [InlineData("notcontoso.com", false)]
    [InlineData("a.contoso.com.evil.com", false)]
    public void A_wildcard_covers_subdomains_but_not_the_bare_name_or_a_lookalike(string host, bool expected) =>
        HostAllowListMiddleware.Matches(["*.contoso.com"], host).Should().Be(expected);
}
