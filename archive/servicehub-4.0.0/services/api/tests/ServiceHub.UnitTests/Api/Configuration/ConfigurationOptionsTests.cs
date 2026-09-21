using FluentAssertions;
using ServiceHub.Api.Configuration;

namespace ServiceHub.UnitTests.Api.Configuration;

public class HttpHeadersOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new HttpHeadersOptions();
        options.CorrelationId.Should().Be("X-Correlation-Id");
        options.RateLimitLimit.Should().Be("X-RateLimit-Limit");
        options.RateLimitRemaining.Should().Be("X-RateLimit-Remaining");
        options.RateLimitReset.Should().Be("X-RateLimit-Reset");
        options.TotalCount.Should().Be("X-Total-Count");
        options.PageNumber.Should().Be("X-Page-Number");
        options.PageSize.Should().Be("X-Page-Size");
    }

    [Fact]
    public void GetExposedHeaders_ReturnsAllHeaders()
    {
        var options = new HttpHeadersOptions();
        var headers = options.GetExposedHeaders();
        headers.Should().HaveCount(7);
        headers.Should().Contain("X-Correlation-Id");
        headers.Should().Contain("X-RateLimit-Limit");
        headers.Should().Contain("X-RateLimit-Remaining");
        headers.Should().Contain("X-RateLimit-Reset");
        headers.Should().Contain("X-Total-Count");
        headers.Should().Contain("X-Page-Number");
        headers.Should().Contain("X-Page-Size");
    }

    [Fact]
    public void GetExposedHeaders_ReflectsCustomValues()
    {
        var options = new HttpHeadersOptions
        {
            CorrelationId = "X-Custom-Correlation",
            TotalCount = "X-Custom-Total"
        };
        var headers = options.GetExposedHeaders();
        headers.Should().Contain("X-Custom-Correlation");
        headers.Should().Contain("X-Custom-Total");
    }
}

public class SecurityHeadersOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new SecurityHeadersOptions();
        options.ContentTypeOptions.Should().Be("nosniff");
        options.FrameOptions.Should().Be("DENY");
        options.ReferrerPolicy.Should().Be("strict-origin-when-cross-origin");
        options.XssProtection.Should().Be("1; mode=block");
        options.Enabled.Should().BeTrue();
        options.ApiVersion.Should().Be("1.0");
        options.StrictTransportSecurity.Should().Contain("max-age=");
        options.PermissionsPolicy.Should().Contain("camera=()");
        options.ContentSecurityPolicyProduction.Should().Contain("default-src");
        options.ContentSecurityPolicyDevelopment.Should().Contain("unsafe-inline");
    }

    [Fact]
    public void Properties_CanBeCustomized()
    {
        var options = new SecurityHeadersOptions
        {
            ContentTypeOptions = "custom",
            FrameOptions = "SAMEORIGIN",
            Enabled = false,
            ApiVersion = "2.0"
        };
        options.ContentTypeOptions.Should().Be("custom");
        options.FrameOptions.Should().Be("SAMEORIGIN");
        options.Enabled.Should().BeFalse();
        options.ApiVersion.Should().Be("2.0");
    }
}
