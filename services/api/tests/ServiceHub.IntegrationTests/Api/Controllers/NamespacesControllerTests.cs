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

public sealed class NamespacesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/v1/namespaces";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public NamespacesControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string UniqueId() => Guid.NewGuid().ToString("N")[..8];

    private static CreateNamespaceRequest MakeRequest(string prefix, string? displayName = null, string? description = null) =>
        new(
            Name: $"{prefix}-{UniqueId()}.servicebus.windows.net",
            ConnectionString: $"Endpoint=sb://{prefix}-{UniqueId()}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString,
            DisplayName: displayName,
            Description: description);

    [Fact]
    public async Task GetAll_ShouldReturnOk()
    {
        var response = await _client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var namespaces = await response.Content.ReadFromJsonAsync<List<NamespaceResponse>>(JsonOptions);
        namespaces.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WithValidRequest_ShouldReturnCreated()
    {
        var request = MakeRequest("create", displayName: "Test Namespace", description: "Test description");

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be(request.Name.ToLowerInvariant());
        created.DisplayName.Should().Be(request.DisplayName);
    }

    [Fact]
    public async Task Create_WithDuplicateName_ShouldReturnConflict()
    {
        var name = $"dup-{UniqueId()}.servicebus.windows.net";
        var connStr = $"Endpoint=sb://dup-{UniqueId()}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=";
        var request = new CreateNamespaceRequest(
            Name: name,
            ConnectionString: connStr,
            AuthType: ConnectionAuthType.ConnectionString);

        await _client.PostAsJsonAsync(BaseUrl, request);
        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithInvalidConnectionString_ShouldReturnBadRequest()
    {
        var request = new CreateNamespaceRequest(
            Name: $"invalid-{UniqueId()}.servicebus.windows.net",
            ConnectionString: "invalid-connection-string",
            AuthType: ConnectionAuthType.ConnectionString);

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithEmptyName_ShouldReturnBadRequest()
    {
        var request = new CreateNamespaceRequest(
            Name: "",
            ConnectionString: $"Endpoint=sb://test-{UniqueId()}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString);

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_WithExistingNamespace_ShouldReturnOk()
    {
        var request = MakeRequest("getbyid");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var response = await _client.GetAsync($"{BaseUrl}/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await response.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetById_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await _client.GetAsync($"{BaseUrl}/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_WithExistingNamespace_ShouldReturnNoContent()
    {
        var request = MakeRequest("deltest");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var response = await _client.DeleteAsync($"{BaseUrl}/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await _client.DeleteAsync($"{BaseUrl}/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
