using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Infrastructure;

/// <summary>
/// Sibling to <see cref="TestWebApplicationFactory"/> with API key authentication enabled and two
/// scoped identities configured, so tests can drive requests as two distinct owners. A scoped key
/// with an explicit "admin" scope (as opposed to no scopes at all) still passes every
/// <c>RequireScope</c> check via the admin-grants-everything rule, but — unlike an implicit admin
/// key — gets its own SHA-256-derived owner ID (see <c>ApiKeyAuthenticationMiddleware.ComputeScopedOwnerId</c>),
/// so the two keys resolve to two isolated tenants instead of collapsing onto the shared SPA owner.
/// </summary>
public sealed class TenantIsolationWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string OwnerAApiKey = "tenant-isolation-test-owner-a-key";
    public const string OwnerBApiKey = "tenant-isolation-test-owner-b-key";

    private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-tenant-isolation-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-encryption-key-for-integration-tests-minimum-32bytes",
                ["Security:EnableConnectionStringEncryption"] = "true",
                ["Security:SpaToken:Enabled"] = "false",
                ["Security:Authentication:Enabled"] = "true",
                ["Security:Authentication:ScopedApiKeys:0:Key"] = OwnerAApiKey,
                ["Security:Authentication:ScopedApiKeys:0:Scopes:0"] = "admin",
                ["Security:Authentication:ScopedApiKeys:0:Description"] = "Tenant isolation test - owner A",
                ["Security:Authentication:ScopedApiKeys:1:Key"] = OwnerBApiKey,
                ["Security:Authentication:ScopedApiKeys:1:Scopes:0"] = "admin",
                ["Security:Authentication:ScopedApiKeys:1:Description"] = "Tenant isolation test - owner B",
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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }

    public HttpClient CreateOwnerAClient() => CreateClientWithKey(OwnerAApiKey);

    public HttpClient CreateOwnerBClient() => CreateClientWithKey(OwnerBApiKey);

    private HttpClient CreateClientWithKey(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
        return client;
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
