using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceHub.IntegrationTests.Api.Configuration;

/// <summary>
/// Reproduces the actual layered configuration (appsettings.json + appsettings.{Environment}.json,
/// exactly as the real host builds it) rather than asserting on source alone: .NET's JSON
/// configuration provider flattens arrays into indexed keys, so an empty
/// "Cors:AllowedOrigins": [] in appsettings.Production.json cannot clear indexed keys inherited
/// from the base appsettings.json. These tests never set "Cors:AllowedOrigins" as a config
/// override themselves — that would mask the exact bug being verified — and instead read the
/// real CORS behavior off actual HTTP responses.
/// </summary>
public sealed class CorsOriginLayeringTests
{
    private const string LocalhostOrigin = "http://localhost:3000";

    [Fact]
    public async Task Development_LocalhostOrigin_IsAllowed()
    {
        using var factory = new EnvironmentWebApplicationFactory("Development");
        using var client = factory.CreateClient();

        var response = await SendWithOriginAsync(client, LocalhostOrigin);

        response.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Production_LocalhostOrigin_WithNoExplicitCorsConfig_IsNotAllowed()
    {
        using var factory = new EnvironmentWebApplicationFactory("Production");
        using var client = factory.CreateClient();

        var response = await SendWithOriginAsync(client, LocalhostOrigin);

        response.Headers.Should().NotContain(h => h.Key == "Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Staging_LocalhostOrigin_WithNoExplicitCorsConfig_IsNotAllowed()
    {
        // No appsettings.Staging.json exists — Staging has nothing of its own to fall back on,
        // so this proves the base appsettings.json genuinely carries no default origins.
        using var factory = new EnvironmentWebApplicationFactory("Staging");
        using var client = factory.CreateClient();

        var response = await SendWithOriginAsync(client, LocalhostOrigin);

        response.Headers.Should().NotContain(h => h.Key == "Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Production_WithExplicitConfiguredOrigin_AllowsOnlyThatOrigin()
    {
        const string configuredOrigin = "https://servicehub.example.com";
        using var factory = new EnvironmentWebApplicationFactory(
            "Production", new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = configuredOrigin });
        using var client = factory.CreateClient();

        var configuredResponse = await SendWithOriginAsync(client, configuredOrigin);
        var localhostResponse = await SendWithOriginAsync(client, LocalhostOrigin);

        configuredResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(configuredOrigin);
        localhostResponse.Headers.Should().NotContain(h => h.Key == "Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Production_AllowedOrigin_StillReceivesAllowCredentials()
    {
        const string configuredOrigin = "https://servicehub.example.com";
        using var factory = new EnvironmentWebApplicationFactory(
            "Production", new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = configuredOrigin });
        using var client = factory.CreateClient();

        var response = await SendWithOriginAsync(client, configuredOrigin);

        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle("true");
    }

    private static async Task<HttpResponseMessage> SendWithOriginAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Boots the real API host under a given ASP.NET Core environment name, supplying only the
    /// non-CORS settings needed for the app to start (mirrors ProductionConfigurationTests'
    /// factories). Deliberately does not set "Cors:AllowedOrigins" unless a test explicitly asks
    /// for that override, so the real appsettings.json + appsettings.{Environment}.json layering
    /// is what determines the effective CORS origins.
    /// </summary>
    private sealed class EnvironmentWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _environmentName;
        private readonly Dictionary<string, string?> _configOverrides;
        private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-test-{Guid.NewGuid():N}");

        public EnvironmentWebApplicationFactory(string environmentName, Dictionary<string, string?>? overrides = null)
        {
            _environmentName = environmentName;
            _configOverrides = overrides ?? [];
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // CorsConfiguration.AddCorsConfiguration reads Cors:AllowedOrigins eagerly during
            // ConfigureServices, which — for this minimal-hosting Program.cs — runs before
            // WebApplicationFactory's Build() finishes applying ConfigureAppConfiguration/
            // AddInMemoryCollection overrides. UseSetting is merged in early enough to be visible
            // to that eager read (it's how Configuration:SkipLocalSettings below already works).
            var defaults = new Dictionary<string, string?>
            {
                // Must match the Host header WebApplicationFactory's TestServer client sends
                // ("localhost", no port) — ASP.NET Core's automatic host-filtering middleware
                // runs before CORS and would otherwise 400 every request in this test.
                ["AllowedHosts"] = "localhost",
                ["Security:EncryptionKey"] = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d",
                ["Security:EnableConnectionStringEncryption"] = "true",
                ["SiteUrl"] = "https://servicehub.example.com",
                ["Security:SpaToken:Enabled"] = "true",
                ["Security:SpaToken:Secret"] = "test-token-secret-minimum-32-chars--",
                ["Security:Authentication:Enabled"] = "false",
                ["Security:SecurityHeaders:Enabled"] = "true",
                ["RateLimiting:Enabled"] = "false",
                ["NamespaceRepository:DataDirectory"] = _testDataDir,
                ["DlqDatabase:DataDirectory"] = _testDataDir,
                ["Configuration:SkipLocalSettings"] = "true",
            };

            foreach (var kvp in _configOverrides)
            {
                defaults[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in defaults)
            {
                builder.UseSetting(kvp.Key, kvp.Value);
            }

            builder.UseEnvironment(_environmentName);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_testDataDir))
            {
                try { Directory.Delete(_testDataDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}
