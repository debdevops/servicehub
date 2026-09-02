using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Configuration;

namespace ServiceHub.UnitTests.Api.Configuration;

public class ProductionConfigurationValidatorTests
{
    private readonly Mock<IHostEnvironment> _environment;
    private readonly Mock<ILogger> _logger;

    public ProductionConfigurationValidatorTests()
    {
        _environment = new Mock<IHostEnvironment>();
        _environment.Setup(e => e.EnvironmentName).Returns("Production");
        _logger = new Mock<ILogger>();
    }

    private static Dictionary<string, string?> BaselineValidConfig() => new()
    {
        ["AllowedHosts"] = "servicehub.example.com",
        ["Security:EncryptionKey"] = new string('a', 64),
        ["SiteUrl"] = "https://servicehub.example.com",
        ["Security:SpaToken:Secret"] = new string('b', 32),
        ["Security:Authentication:Enabled"] = "true",
    };

    private void Validate(Dictionary<string, string?> overrides)
    {
        var dict = BaselineValidConfig();
        foreach (var (key, value) in overrides)
        {
            dict[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        ProductionConfigurationValidator.ValidateProduction(configuration, _environment.Object, _logger.Object);
    }

    [Fact]
    public void ValidateProduction_NamedLegacyApiKey_IsRecognizedAsConfiguredAuthentication()
    {
        // Regression test: HasConfiguredApiKeys used to bind Security:Authentication:ApiKeys via
        // Get<string[]>(), which silently produced no usable value for a named
        // ({ "Key": ..., "Description": ... }) entry — causing production startup to fail with
        // "no usable authentication method configured" even though a valid key was present.
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Authentication:ApiKeys:0:Key"] = "named-admin-key-12345",
            ["Security:Authentication:ApiKeys:0:Description"] = "Ops bootstrap key",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_PlainLegacyApiKey_IsRecognizedAsConfiguredAuthentication()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Authentication:ApiKeys:0"] = "plain-admin-key-12345",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_NoAuthenticationMethodConfigured_Throws()
    {
        var act = () => Validate([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_SpaTokenEnabledAlone_Throws()
    {
        // SpaTokenProvider's own doc comment says the SPA token confirms same-origin HTML delivery
        // (CSRF mitigation) but does not identify or authenticate a user — anyone who can fetch the
        // index page can read and replay it. It must never satisfy production auth validation on its
        // own; regression guard for the F2 finding.
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:SpaToken:Enabled"] = "true",
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_EasyAuthEnabled_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_OidcConfigured_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Oidc:Enabled"] = "true",
            ["Security:Oidc:Authority"] = "https://issuer.example.com",
            ["Security:Oidc:Audience"] = "servicehub-api",
        });

        act.Should().NotThrow();
    }
}
