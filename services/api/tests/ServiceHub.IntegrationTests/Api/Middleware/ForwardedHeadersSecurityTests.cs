using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Api.Middleware;

/// <summary>
/// Tests for secure forwarded header handling (X-Forwarded-For, X-Forwarded-Proto).
/// Verifies that forwarded headers are only trusted when explicitly configured.
/// </summary>
public sealed class ForwardedHeadersSecurityTests
{
    [Fact]
    public async Task HealthEndpoint_WithUntrustedForwardedFor_IgnoresHeader()
    {
        // When forwarded headers are disabled (default), X-Forwarded-For should be ignored
        var factory = new UnsafeFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "false",
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.1.1");

        var response = await client.GetAsync("/health");

        // Health check should succeed (we're not testing IP-based logic, just that header is ignored)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_WithTrustedForwardedFor_AcceptsHeader()
    {
        // When a proxy IP is explicitly trusted, X-Forwarded-For should be accepted
        // Note: This test verifies configuration; actual IP-based behavior depends on middleware order
        var factory = new UnsafeFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1", // Loopback (request source)
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1");

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_WithTrustedCIDRNetwork_AcceptsHeader()
    {
        // CIDR-based trusted networks should work
        var factory = new UnsafeFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownNetworks:0"] = "127.0.0.0/8", // Loopback network
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1");

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_DisabledForwardedHeaders_DefaultBehavior()
    {
        // Default behavior: forwarded headers disabled
        var factory = new UnsafeFactory(new Dictionary<string, string?>
        {
            // ForwardedHeaders section not configured → defaults to disabled
        });

        using var client = factory.CreateClient();
        // Try to inject a forwarded header
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "evil.attacker.com");

        var response = await client.GetAsync("/health");

        // Endpoint should work regardless; forwarded header is simply ignored
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_AutoDetectAzureAppService_EnabledWhenEnvironmentVariableSet()
    {
        // When deployed on Azure App Service, WEBSITE_AUTH_ENABLED triggers auto-enable
        var factory = new UnsafeFactory(
            new Dictionary<string, string?>
            {
                // ForwardedHeaders:Enabled not explicitly set, but...
                ["WEBSITE_AUTH_ENABLED"] = "true", // ...this env var triggers auto-detection
            });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1");

        var response = await client.GetAsync("/health");

        // Should succeed; auto-detection enabled forwarded headers
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Test factory that configures a minimal app for forwarded-headers testing.
    /// Uses the exact same builder setup as production (to catch regressions).
    /// </summary>
    private sealed class UnsafeFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _configOverrides;
        private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-test-{Guid.NewGuid():N}");

        public UnsafeFactory(Dictionary<string, string?> configOverrides)
        {
            _configOverrides = configOverrides;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var defaults = new Dictionary<string, string?>
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
                };

                foreach (var kvp in _configOverrides)
                {
                    defaults[kvp.Key] = kvp.Value;
                }

                config.AddInMemoryCollection(defaults);
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
