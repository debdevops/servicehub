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
/// End-to-end namespace creation for AWS and GCP: with the provider flags on, valid
/// AWS access key pairs and GCP service account JSON keys create namespaces; malformed
/// credentials are rejected by provider-aware validation; and with the flags off, the
/// create endpoint refuses to mint namespaces nothing can serve.
/// </summary>
public sealed class NamespaceMulticloudCreationTests : IClassFixture<CloudBridgeEnabledProvidersFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/v1/namespaces";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ValidSecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    public NamespaceMulticloudCreationTests(CloudBridgeEnabledProvidersFactory factory)
    {
        _client = factory.CreateClient();
    }

    // Unique per call so name/credential-hash duplicate checks never collide across tests.
    private static string UniqueHex() => Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    // Unique subdomain segment so name-conflict checks never collide across tests.
    private static CreateNamespaceRequest AwsRequest(string connectionString) =>
        new(
            Name: $"sqs.us-east-1.t{Guid.NewGuid().ToString("N")[..10]}.amazonaws.com",
            ConnectionString: connectionString,
            AuthType: ConnectionAuthType.AwsAccessKey)
        {
            Provider = CloudProviderType.Aws,
            AwsRegion = "us-east-1",
        };

    private static CreateNamespaceRequest GcpRequest(string connectionString)
    {
        var projectId = $"gcp-it-{Guid.NewGuid().ToString("N")[..12]}";
        return new CreateNamespaceRequest(
            Name: projectId,
            ConnectionString: connectionString,
            AuthType: ConnectionAuthType.GcpServiceAccount)
        {
            Provider = CloudProviderType.Gcp,
            GcpProjectId = projectId,
        };
    }

    private static string ServiceAccountJson(string projectId = "my-project-123") =>
        $$"""
        {
          "type": "service_account",
          "project_id": "{{projectId}}",
          "client_email": "svc@{{projectId}}.iam.gserviceaccount.com",
          "private_key": "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n"
        }
        """;

    [Fact]
    public async Task Create_AwsNamespace_WithValidAccessKeyPair_ReturnsCreated()
    {
        var request = AwsRequest($"AKIA{UniqueHex()}:{ValidSecretKey}");

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        created.Should().NotBeNull();
        created!.Provider.Should().Be(CloudProviderType.Aws);
        created.AwsRegion.Should().Be("us-east-1");
    }

    [Theory]
    [InlineData("not-a-credential-pair")]
    [InlineData("BKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")]
    [InlineData("AKIAIOSFODNN7EXAMPLE:too-short")]
    public async Task Create_AwsNamespace_WithMalformedCredentials_ReturnsBadRequest(string connectionString)
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, AwsRequest(connectionString));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_AwsNamespace_WithTemporarySessionKey_ReturnsBadRequestNamingAsia()
    {
        var response = await _client.PostAsJsonAsync(
            BaseUrl, AwsRequest($"ASIA{UniqueHex()}:{ValidSecretKey}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Temporary session credentials");
    }

    [Fact]
    public async Task Create_GcpNamespace_WithValidServiceAccountJson_ReturnsCreated()
    {
        var request = GcpRequest(ServiceAccountJson());

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        created.Should().NotBeNull();
        created!.Provider.Should().Be(CloudProviderType.Gcp);
        created.GcpProjectId.Should().Be(request.GcpProjectId);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("""{ "type": "authorized_user", "project_id": "p-123456", "client_email": "a@b.c", "private_key": "PRIVATE KEY" }""")]
    [InlineData("""{ "type": "service_account", "project_id": "p-123456" }""")]
    public async Task Create_GcpNamespace_WithMalformedServiceAccountJson_ReturnsBadRequest(string connectionString)
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, GcpRequest(connectionString));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithMismatchedAuthTypeAndProvider_ReturnsBadRequest()
    {
        // AwsAccessKey auth on an Azure namespace must be rejected outright.
        var request = new CreateNamespaceRequest(
            Name: $"mismatch-{Guid.NewGuid().ToString("N")[..8]}.servicebus.windows.net",
            ConnectionString: $"AKIA{UniqueHex()}:{ValidSecretKey}",
            AuthType: ConnectionAuthType.AwsAccessKey);

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_AzureNamespace_IsUnaffectedByProviderBranching()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var request = new CreateNamespaceRequest(
            Name: $"azure-{unique}.servicebus.windows.net",
            ConnectionString: $"Endpoint=sb://azure-{unique}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString);

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        created!.Provider.Should().Be(CloudProviderType.Azure);
    }
}

/// <summary>
/// With the provider flags off (default TestWebApplicationFactory config), creating an
/// AWS or GCP namespace is refused with 503 so no namespace record can exist that no
/// registered provider can serve. Azure creation is untouched.
/// </summary>
public sealed class NamespaceCreationProviderDisabledTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/v1/namespaces";

    public NamespaceCreationProviderDisabledTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_AwsNamespace_WhenProviderDisabled_Returns503WithFlagGuidance()
    {
        var request = new CreateNamespaceRequest(
            Name: $"sqs.us-east-1.t{Guid.NewGuid().ToString("N")[..10]}.amazonaws.com",
            ConnectionString: "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            AuthType: ConnectionAuthType.AwsAccessKey)
        {
            Provider = CloudProviderType.Aws,
            AwsRegion = "us-east-1",
        };

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CloudProviders:Aws:Enabled");
    }

    [Fact]
    public async Task Create_GcpNamespace_WhenProviderDisabled_Returns503()
    {
        var projectId = $"gcp-off-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new CreateNamespaceRequest(
            Name: projectId,
            ConnectionString: """{ "type": "service_account", "project_id": "p-123456", "client_email": "a@b.c", "private_key": "PRIVATE KEY" }""",
            AuthType: ConnectionAuthType.GcpServiceAccount)
        {
            Provider = CloudProviderType.Gcp,
            GcpProjectId = projectId,
        };

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
