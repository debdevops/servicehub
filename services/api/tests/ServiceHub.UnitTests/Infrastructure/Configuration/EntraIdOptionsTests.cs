using FluentAssertions;
using ServiceHub.Infrastructure.Configuration;

namespace ServiceHub.UnitTests.Infrastructure.Configuration;

public sealed class EntraIdOptionsTests
{
    [Fact]
    public void SectionName_IsCorrect()
    {
        EntraIdOptions.SectionName.Should().Be("EntraId");
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var opts = new EntraIdOptions();

        opts.ClientId.Should().BeEmpty();
        opts.ClientSecret.Should().BeEmpty();
        opts.TenantId.Should().BeEmpty();
        opts.Enabled.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenAllFieldsSetAndEnabled()
    {
        var opts = new EntraIdOptions
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            TenantId = "tenant-id",
        };

        opts.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenDisabled()
    {
        var opts = new EntraIdOptions
        {
            Enabled = false,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            TenantId = "tenant-id",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenClientIdWhitespace()
    {
        var opts = new EntraIdOptions
        {
            Enabled = true,
            ClientId = "  ",
            ClientSecret = "secret",
            TenantId = "tenant",
        };

        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsDefaultCredentialMode_ReturnsTrue_WhenEnabledAndNoCredentials()
    {
        var opts = new EntraIdOptions { Enabled = true };

        opts.IsDefaultCredentialMode.Should().BeTrue();
    }

    [Fact]
    public void IsDefaultCredentialMode_ReturnsFalse_WhenDisabled()
    {
        var opts = new EntraIdOptions();

        opts.IsDefaultCredentialMode.Should().BeFalse();
    }

    [Fact]
    public void IsDefaultCredentialMode_ReturnsFalse_WhenCredentialsProvided()
    {
        var opts = new EntraIdOptions
        {
            Enabled = true,
            ClientId = "client-id",
        };

        opts.IsDefaultCredentialMode.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_WhenFullyConfigured()
    {
        var opts = new EntraIdOptions
        {
            Enabled = true,
            ClientId = "id",
            ClientSecret = "secret",
            TenantId = "tenant",
        };

        opts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_WhenDefaultCredentialMode()
    {
        var opts = new EntraIdOptions { Enabled = true };

        opts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenNeitherConfiguredNorDefaultMode()
    {
        var opts = new EntraIdOptions();

        opts.IsAvailable.Should().BeFalse();
    }
}
