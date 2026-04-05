using FluentAssertions;
using ServiceHub.Infrastructure.Configuration;

namespace ServiceHub.UnitTests.Infrastructure.Configuration;

public sealed class OAuthOptionsTests
{
    [Fact]
    public void IsConfigured_ReturnsTrue_WhenAllFieldsSetAndEnabled()
    {
        var opts = new OAuthOptions
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://app.example.com/api/v1/auth/azure/callback",
            FrontendBaseUrl = "https://app.example.com",
        };

        opts.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenDisabled()
    {
        var opts = new OAuthOptions
        {
            Enabled = false,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://example.com/callback",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenClientIdEmpty()
    {
        var opts = new OAuthOptions
        {
            Enabled = true,
            ClientId = "",
            ClientSecret = "secret",
            RedirectUri = "https://example.com/cb",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenClientSecretEmpty()
    {
        var opts = new OAuthOptions
        {
            Enabled = true,
            ClientId = "id",
            ClientSecret = " ",
            RedirectUri = "https://example.com/cb",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenRedirectUriEmpty()
    {
        var opts = new OAuthOptions
        {
            Enabled = true,
            ClientId = "id",
            ClientSecret = "secret",
            RedirectUri = "",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var opts = new OAuthOptions();

        opts.ClientId.Should().BeEmpty();
        opts.ClientSecret.Should().BeEmpty();
        opts.RedirectUri.Should().BeEmpty();
        opts.Enabled.Should().BeFalse();
        opts.FrontendBaseUrl.Should().Be("http://localhost:3000");
    }

    [Fact]
    public void SectionName_IsCorrect()
    {
        OAuthOptions.SectionName.Should().Be("AzureOAuth");
    }
}
