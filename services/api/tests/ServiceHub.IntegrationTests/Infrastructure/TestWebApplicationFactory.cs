using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ServiceHub.IntegrationTests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-encryption-key-for-integration-tests-minimum-32bytes",
                ["Security:EnableConnectionStringEncryption"] = "true",
                ["Security:Authentication:Enabled"] = "false",
                ["Security:SecurityHeaders:Enabled"] = "true",
                ["Cors:AllowedOrigins:0"] = "*",
                ["RateLimiting:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override any services if needed for testing
        });

        // Use Development environment to skip rate limiting middleware
        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }
}
