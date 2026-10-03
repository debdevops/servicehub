using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// Executable specification of Epic 2 (per-namespace RBAC scoping): a scoped API key with a
/// <c>Namespaces</c> allow-list must see exactly that subset of the namespaces it would otherwise
/// have full access to — access can only narrow, never widen, and the boundary must hold for both
/// per-ID lookups and listing.
/// </summary>
public sealed class NamespaceAllowListTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task RestrictedKey_SeesOnlyAllowListedNamespace_ForGetByIdAndListing()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-namespace-allowlist-test-{Guid.NewGuid():N}");
        try
        {
            Guid allowedNamespaceId;
            Guid otherNamespaceId;

            // Phase 1: unrestricted boot creates two namespaces and captures their server-assigned IDs.
            using (var seedFactory = new NamespaceAllowListWebApplicationFactory(dataDir))
            {
                using var seedClient = seedFactory.CreateClientWithKey();
                allowedNamespaceId = await CreateNamespaceAsync(seedClient, "allowlist-test-allowed");
                otherNamespaceId = await CreateNamespaceAsync(seedClient, "allowlist-test-other");
            }

            // Phase 2: reboot against the same data directory, this time restricted to one namespace —
            // mirroring an operator editing ScopedApiKeys config and restarting the instance.
            using var restrictedFactory = new NamespaceAllowListWebApplicationFactory(
                dataDir, [allowedNamespaceId.ToString()]);
            using var restrictedClient = restrictedFactory.CreateClientWithKey();

            var allowedResponse = await restrictedClient.GetAsync($"/api/v1/namespaces/{allowedNamespaceId}");
            allowedResponse.StatusCode.Should().Be(
                HttpStatusCode.OK, "the allow-listed namespace must remain reachable");

            var disallowedResponse = await restrictedClient.GetAsync($"/api/v1/namespaces/{otherNamespaceId}");
            disallowedResponse.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                "a namespace this key truly owns but that is outside its allow-list must be hidden, " +
                "the same as if it didn't exist");

            var listResponse = await restrictedClient.GetAsync("/api/v1/namespaces");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var namespaces = await listResponse.Content.ReadFromJsonAsync<List<NamespaceResponse>>(JsonOptions);
            namespaces.Should().ContainSingle(n => n.Id == allowedNamespaceId);
            namespaces.Should().NotContain(n => n.Id == otherNamespaceId);
        }
        finally
        {
            if (Directory.Exists(dataDir))
            {
                try { Directory.Delete(dataDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static async Task<Guid> CreateNamespaceAsync(HttpClient client, string namePrefix)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var request = new CreateNamespaceRequest(
            Name: $"{namePrefix}-{unique}.servicebus.windows.net",
            ConnectionString: $"Endpoint=sb://{namePrefix}-{unique}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString);

        var createResponse = await client.PostAsJsonAsync("/api/v1/namespaces", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "namespace seeding must succeed for the test to be meaningful");

        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        return created!.Id;
    }
}
