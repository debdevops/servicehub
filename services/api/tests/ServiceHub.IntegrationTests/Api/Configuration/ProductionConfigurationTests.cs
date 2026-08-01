using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Api.Configuration;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void ProductionApp_WithMissingAllowedHosts_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = null, // Missing
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithPlaceholderAllowedHosts_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "SET_VIA_ENV_VAR",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithWildcardAllowedHosts_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithMissingEncryptionKey_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = null, // Missing
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithPlaceholderEncryptionKey_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "SET_VIA_ENV_VAR",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithInvalidEncryptionKeyFormat_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "not-a-valid-hex-key", // Not 64 hex chars
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithMissingSiteUrl_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = null, // Missing
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithInvalidSiteUrlFormat_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "not-a-valid-url", // Not a URL
            ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithMissingSpaTokenSecret_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = null, // Missing
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithShortSpaTokenSecret_ShouldFailAtStartup()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "short-secret", // Too short
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production configuration is incomplete*");
    }

    [Fact]
    public void ProductionApp_WithValidConfiguration_ShouldStartSuccessfully()
    {
        var factory = new ProductionTestWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "servicehub.example.com;www.servicehub.example.com",
            ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
            ["SiteUrl"] = "https://servicehub.example.com",
            ["Security:SpaToken:Secret"] = "test-token-secret-must-be-at-least-32-chars-long",
        });

        var action = () => factory.CreateClient();

        action.Should().NotThrow();
    }

    [Fact]
    public void DevelopmentApp_SkipsProductionValidation_CanStartWithInvalidConfig()
    {
        // Use the standard test factory which runs in Development environment
        var factory = new DevelopmentTestWebApplicationFactory();

        var action = () => factory.CreateClient();

        // Should NOT throw even though AllowedHosts would be invalid in Production
        action.Should().NotThrow();
    }

    private sealed class ProductionTestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _configOverrides;
        private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-test-{Guid.NewGuid():N}");

        public ProductionTestWebApplicationFactory(Dictionary<string, string?> configOverrides)
        {
            _configOverrides = configOverrides;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var defaults = new Dictionary<string, string?>
                {
                    ["Security:EnableConnectionStringEncryption"] = "true",
                    ["Security:SpaToken:Enabled"] = "true",
                    ["Security:Authentication:Enabled"] = "false",
                    ["Security:SecurityHeaders:Enabled"] = "true",
                    ["Cors:AllowedOrigins:0"] = "https://servicehub.example.com",
                    ["RateLimiting:Enabled"] = "false",
                    ["NamespaceRepository:DataDirectory"] = _testDataDir,
                    ["DlqDatabase:DataDirectory"] = _testDataDir
                };

                // Merge overrides
                foreach (var kvp in _configOverrides)
                {
                    defaults[kvp.Key] = kvp.Value;
                }

                config.AddInMemoryCollection(defaults);
            });

            builder.UseSetting("Configuration:SkipLocalSettings", "true");
            // CRITICAL: Run in Production environment so validation is triggered
            builder.UseEnvironment("Production");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_testDataDir))
            {
                try { Directory.Delete(_testDataDir, recursive: true); }
                catch { }
            }
        }
    }

    private sealed class DevelopmentTestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-test-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:EncryptionKey"] = "test-encryption-key-for-integration-tests-minimum-32bytes",
                    ["Security:EnableConnectionStringEncryption"] = "true",
                    ["Security:SpaToken:Enabled"] = "false",
                    ["Security:Authentication:Enabled"] = "false",
                    ["Security:SecurityHeaders:Enabled"] = "true",
                    ["Cors:AllowedOrigins:0"] = "*",
                    ["RateLimiting:Enabled"] = "false",
                    ["NamespaceRepository:DataDirectory"] = _testDataDir,
                    ["DlqDatabase:DataDirectory"] = _testDataDir
                });
            });

            builder.UseSetting("Configuration:SkipLocalSettings", "true");
            builder.UseEnvironment("Development");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_testDataDir))
            {
                try { Directory.Delete(_testDataDir, recursive: true); }
                catch { }
            }
        }
    }
}
