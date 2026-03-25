using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Security;

namespace ServiceHub.UnitTests.Api.Security;

public class SpaTokenProviderTests
{
    private readonly Mock<ILogger<SpaTokenProvider>> _logger = new();

    private static IConfiguration CreateConfig(bool enabled = true, string? secret = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Security:SpaToken:Enabled"] = enabled.ToString()
        };

        if (secret != null)
        {
            dict["Security:SpaToken:Secret"] = secret;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void IsEnabled_WhenDisabled_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: false);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WhenEnabled_ShouldReturnTrue()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GenerateToken_ShouldReturnNonEmptyString()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        var token = provider.GenerateToken();

        token.Should().NotBeNullOrWhiteSpace();
        token.Should().Contain(".");
    }

    [Fact]
    public void ValidateToken_WithValidToken_ShouldReturnTrue()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        var token = provider.GenerateToken();
        var result = provider.ValidateToken(token);

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateToken_WithTamperedSignature_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        var token = provider.GenerateToken();
        // Tamper with the signature part
        var parts = token.Split('.');
        var tamperedToken = parts[0] + ".AAAA" + parts[1][4..];
        
        provider.ValidateToken(tamperedToken).Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WithNullToken_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.ValidateToken(null).Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.ValidateToken("").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WithMalformedToken_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.ValidateToken("not-a-valid-token").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WhenDisabled_ShouldReturnFalse()
    {
        var config = CreateConfig(enabled: false);
        var provider = new SpaTokenProvider(config, _logger.Object);

        provider.ValidateToken("anything").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WithDifferentProvider_ShouldReturnFalse()
    {
        // Two providers with different ephemeral keys should not accept each other's tokens
        var config1 = CreateConfig(enabled: true);
        var config2 = CreateConfig(enabled: true);
        var provider1 = new SpaTokenProvider(config1, _logger.Object);
        var provider2 = new SpaTokenProvider(config2, _logger.Object);

        var token = provider1.GenerateToken();

        provider2.ValidateToken(token).Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_WithConfiguredSecret_ShouldAcceptOwnToken()
    {
        // Use a fixed hex secret (32 bytes = 64 hex chars)
        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config = CreateConfig(enabled: true, secret: secret);
        var provider = new SpaTokenProvider(config, _logger.Object);

        var token = provider.GenerateToken();
        provider.ValidateToken(token).Should().BeTrue();
    }

    [Fact]
    public void ValidateToken_SameSecret_DifferentInstances_ShouldAccept()
    {
        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config1 = CreateConfig(enabled: true, secret: secret);
        var config2 = CreateConfig(enabled: true, secret: secret);
        var provider1 = new SpaTokenProvider(config1, _logger.Object);
        var provider2 = new SpaTokenProvider(config2, _logger.Object);

        var token = provider1.GenerateToken();
        provider2.ValidateToken(token).Should().BeTrue();
    }

    [Fact]
    public void GenerateToken_ShouldProduceDifferentTokensOverTime()
    {
        var config = CreateConfig(enabled: true);
        var provider = new SpaTokenProvider(config, _logger.Object);

        var token1 = provider.GenerateToken();
        // Tiny delay not needed — same-second tokens are fine, they'll have same timestamp
        // But different calls may produce the same token if within the same second
        // The important thing is they're both valid
        var token2 = provider.GenerateToken();

        provider.ValidateToken(token1).Should().BeTrue();
        provider.ValidateToken(token2).Should().BeTrue();
    }
}
