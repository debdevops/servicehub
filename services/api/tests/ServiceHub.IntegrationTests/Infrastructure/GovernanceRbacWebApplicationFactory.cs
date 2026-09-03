using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Infrastructure;

/// <summary>
/// Sibling to <see cref="TenantIsolationWebApplicationFactory"/>, but for the opposite scenario:
/// two named, <b>legacy-style</b> API keys (plain <c>Security:Authentication:ApiKeys</c> entries,
/// not <c>ScopedApiKeys</c>) that deliberately collapse onto the <i>same</i>
/// <c>Namespace.SpaOwnerId</c> partition, differentiated only by their <c>ApiKeyName</c> — the
/// exact "two credentials, one owner scope" shape roadmap item W3.1 targets. Governance/RBAC
/// (<see cref="ServiceHub.Api.Filters.GovernanceAuthorizationFilter"/>) is the only thing that can
/// still tell them apart once both hold the same owner scope and the same admin API-key scope.
/// </summary>
public sealed class GovernanceRbacWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminApiKey = "governance-rbac-test-admin-key";
    public const string ViewerApiKey = "governance-rbac-test-viewer-key";

    /// <summary>Matches <c>ActorIdentityResolver.ResolveHttpActor</c>'s <c>ApiKey:{name}</c>
    /// convention for the admin key's configured <c>Description</c>.</summary>
    public const string AdminGranteeIdentity = "ApiKey:Governance RBAC test - admin";

    /// <summary>Matches <c>ActorIdentityResolver.ResolveHttpActor</c>'s <c>ApiKey:{name}</c>
    /// convention for the viewer key's configured <c>Description</c>.</summary>
    public const string ViewerGranteeIdentity = "ApiKey:Governance RBAC test - viewer";

    private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-governance-rbac-test-{Guid.NewGuid():N}");

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
                // Named legacy entries (object form, not a plain string) so each gets its own
                // Description — and therefore its own ApiKeyName/GranteeIdentity — while both
                // still resolve to the shared SPA owner, since neither sets Scopes.
                ["Security:Authentication:ApiKeys:0:Key"] = AdminApiKey,
                ["Security:Authentication:ApiKeys:0:Description"] = "Governance RBAC test - admin",
                ["Security:Authentication:ApiKeys:1:Key"] = ViewerApiKey,
                ["Security:Authentication:ApiKeys:1:Description"] = "Governance RBAC test - viewer",
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

    public HttpClient CreateAdminClient() => CreateClientWithKey(AdminApiKey);

    public HttpClient CreateViewerClient() => CreateClientWithKey(ViewerApiKey);

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
