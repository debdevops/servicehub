using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Infrastructure;

/// <summary>
/// Boots a host with a single scoped API key, optionally restricted to a namespace allow-list,
/// pointed at a caller-supplied data directory. Namespace IDs are server-generated, so a test
/// that needs a restricted key scoped to real namespace IDs must boot this factory twice against
/// the same data directory: once unrestricted to create namespaces and capture their IDs, then
/// again with those IDs passed to <paramref name="allowedNamespaceIds"/> — mirroring how an
/// operator would edit <c>ScopedApiKeys</c> config and restart the instance.
/// </summary>
public sealed class NamespaceAllowListWebApplicationFactory(
    string dataDirectory,
    IReadOnlyList<string>? allowedNamespaceIds = null) : WebApplicationFactory<Program>
{
    public const string ApiKey = "namespace-allow-list-test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-encryption-key-for-integration-tests-minimum-32bytes",
                ["Security:EnableConnectionStringEncryption"] = "true",
                ["Security:SpaToken:Enabled"] = "false",
                ["Security:Authentication:Enabled"] = "true",
                ["Security:Authentication:ScopedApiKeys:0:Key"] = ApiKey,
                ["Security:Authentication:ScopedApiKeys:0:Scopes:0"] = "admin",
                ["Security:Authentication:ScopedApiKeys:0:Description"] = "Namespace allow-list test key",
                ["Security:SecurityHeaders:Enabled"] = "true",
                ["Cors:AllowedOrigins:0"] = "*",
                ["RateLimiting:Enabled"] = "false",
                ["NamespaceRepository:DataDirectory"] = dataDirectory,
                ["DlqDatabase:DataDirectory"] = dataDirectory,
            };

            for (var i = 0; i < allowedNamespaceIds?.Count; i++)
            {
                settings[$"Security:Authentication:ScopedApiKeys:0:Namespaces:{i}"] = allowedNamespaceIds[i];
            }

            config.AddInMemoryCollection(settings);
        });

        builder.UseSetting("Configuration:SkipLocalSettings", "true");
        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }

    public HttpClient CreateClientWithKey()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", ApiKey);
        return client;
    }
}
