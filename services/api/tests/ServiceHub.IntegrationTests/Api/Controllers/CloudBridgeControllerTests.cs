using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// WebApplicationFactory with cloud provider feature flags explicitly disabled so that
/// no AWS or GCP providers are registered during the CloudBridge controller tests.
/// </summary>
public sealed class CloudBridgeTestFactory : WebApplicationFactory<Program>
{
    private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-cb-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Add our overrides last so they take precedence over appsettings.Development.json
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
                // Explicitly disable both cloud providers so no external SDK clients are initialised
                ["CloudProviders:Aws:Enabled"] = "false",
                ["CloudProviders:Gcp:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove any ICloudMessagingProvider registrations that may have been added by feature flags.
            // This is necessary because WebApplicationFactory configuration overrides run after the
            // minimal API host has already evaluated the feature flags.
            var descriptors = services
                .Where(d => d.ServiceType == typeof(ServiceHub.Core.Interfaces.ICloudMessagingProvider))
                .ToList();
            foreach (var d in descriptors)
                services.Remove(d);
        });

        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_testDataDir))
        {
            try { Directory.Delete(_testDataDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}

/// <summary>
/// Integration tests for <see cref="ServiceHub.Api.Controllers.V1.CloudBridgeController"/>.
/// These tests validate the HTTP layer using the in-process test server with feature flags disabled.
/// </summary>
public sealed class CloudBridgeControllerTests : IClassFixture<CloudBridgeTestFactory>
{
    private readonly HttpClient _client;

    public CloudBridgeControllerTests(CloudBridgeTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/cloud-bridge/provider-status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProviderStatus_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/cloud-bridge/provider-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProviderStatus_ReturnsBothProvidersDisabled_WhenFlagsOff()
    {
        var response = await _client.GetAsync("/api/v1/cloud-bridge/provider-status");
        var json = await response.Content.ReadAsStringAsync();

        // In the test environment, no providers are registered
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("Aws").GetBoolean().Should().BeFalse();
        root.GetProperty("Gcp").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetProviderStatus_ReturnsJsonContentType()
    {
        var response = await _client.GetAsync("/api/v1/cloud-bridge/provider-status");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/cloud-bridge/namespaces/{id}/entities?provider=Aws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListEntities_WithUnregisteredProvider_Returns404()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/cloud-bridge/namespaces/{namespaceId}/entities?provider=Aws");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListEntities_WithInvalidProvider_Returns400()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/cloud-bridge/namespaces/{namespaceId}/entities?provider=NotValid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListEntities_WithMissingProvider_Returns400()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/cloud-bridge/namespaces/{namespaceId}/entities");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/cloud-bridge/namespaces/{id}/visibility/{queue}?provider=Aws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetVisibilityStatus_WithUnregisteredProvider_Returns404()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{namespaceId}/visibility/my-queue?provider=Aws");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVisibilityStatus_WithInvalidProvider_Returns400()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{namespaceId}/visibility/my-queue?provider=INVALID");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
