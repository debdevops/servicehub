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
        builder.UseSetting("Configuration:SkipLocalSettings", "true");
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
                ["DlqDatabase:DataDirectory"] = _testDataDir,
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
    public async Task ListEntities_WithUnregisteredProvider_Returns503()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/cloud-bridge/namespaces/{namespaceId}/entities?provider=Aws");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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
    public async Task GetVisibilityStatus_WithUnregisteredProvider_Returns503()
    {
        var namespaceId = Guid.NewGuid();
        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{namespaceId}/visibility/my-queue?provider=Aws");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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

/// <summary>
/// WebApplicationFactory with both cloud provider feature flags enabled, proving that
/// Program.cs reads <c>CloudProviders:{Aws,Gcp}:Enabled</c> and registers the live
/// AWS/GCP providers (and their connectivity health checks) when the flags are true.
/// </summary>
public sealed class CloudBridgeEnabledProvidersFactory : WebApplicationFactory<Program>
{
    private readonly string _testDataDir = Path.Combine(Path.GetTempPath(), $"servicehub-cbe-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Configuration:SkipLocalSettings", "true");
        builder.ConfigureAppConfiguration((_, config) =>
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
                ["DlqDatabase:DataDirectory"] = _testDataDir,
            });
        });

        // UseSetting flows into host configuration BEFORE Program.cs executes — the
        // same mechanism UseEnvironment relies on. ConfigureAppConfiguration overrides
        // are appended at Build() time, which is too late for the top-level
        // feature-flag reads in Program.cs.
        builder.UseSetting("CloudProviders:Aws:Enabled", "true");
        builder.UseSetting("CloudProviders:Gcp:Enabled", "true");

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
/// Integration tests for the <c>CloudProviders:{Aws,Gcp}:Enabled</c> feature flags.
/// Registration must be inert with no namespaces configured: providers report as
/// enabled and their health checks run, but nothing calls out to a real cloud.
/// </summary>
public sealed class CloudProviderFlagsEnabledTests : IClassFixture<CloudBridgeEnabledProvidersFactory>
{
    private readonly HttpClient _client;

    public CloudProviderFlagsEnabledTests(CloudBridgeEnabledProvidersFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProviderStatus_ReturnsBothProvidersEnabled_WhenFlagsOn()
    {
        var response = await _client.GetAsync("/api/v1/cloud-bridge/provider-status");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Aws").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("Gcp").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ListEntities_WithEnabledProvider_GetsPastTheNotEnabledGuard()
    {
        // With the flag on, the provider resolves — the request must get past the
        // 503 "provider not enabled" guard and fail on the unknown namespace instead
        // (today that surfaces as 502 via the controller's error mapping).
        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{Guid.NewGuid()}/entities?provider=Aws");

        response.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
        response.IsSuccessStatusCode.Should().BeFalse("the namespace does not exist");
    }

    [Fact]
    public async Task Health_IncludesProviderConnectivityChecks_WhenFlagsOn()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        entries.TryGetProperty("aws-connectivity", out var aws).Should().BeTrue();
        entries.TryGetProperty("gcp-connectivity", out var gcp).Should().BeTrue();
        // No AWS/GCP namespaces exist, so both checks must be Healthy no-ops.
        aws.GetProperty("status").GetString().Should().Be("Healthy");
        gcp.GetProperty("status").GetString().Should().Be("Healthy");
    }
}
