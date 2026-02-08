using System.Net;
using System.Net.Http.Json;
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

    public NamespacesControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_WhenNoNamespaces_ShouldReturnEmptyList()
    {
        var response = await _client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var namespaces = await response.Content.ReadFromJsonAsync<List<NamespaceResponse>>();
        namespaces.Should().NotBeNull();
        namespaces.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithValidRequest_ShouldReturnCreated()
    {
        var request = new CreateNamespaceRequest(
            Name: "test-namespace.servicebus.windows.net",
            ConnectionString: "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123==",
            AuthType: ConnectionAuthType.ConnectionString,
            DisplayName: "Test Namespace",
            Description: "Test description");

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>();
        created.Should().NotBeNull();
        created!.Name.Should().Be(request.Name.ToLowerInvariant());
        created.DisplayName.Should().Be(request.DisplayName);
    }

    [Fact]
    public async Task Create_WithDuplicateName_ShouldReturnConflict()
    {
        var request = new CreateNamespaceRequest(
            Name: "duplicate-namespace.servicebus.windows.net",
            ConnectionString: "Endpoint=sb://duplicate.servicebus.windows.net/;SharedAccessKey=key==",
            AuthType: ConnectionAuthType.ConnectionString);

        await _client.PostAsJsonAsync(BaseUrl, request);
        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithInvalidConnectionString_ShouldReturnBadRequest()
    {
        var request = new CreateNamespaceRequest(
            Name: "test-namespace.servicebus.windows.net",
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
            ConnectionString: "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKey=key==",
            AuthType: ConnectionAuthType.ConnectionString);

        var response = await _client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_WithExistingNamespace_ShouldReturnOk()
    {
        var createRequest = new CreateNamespaceRequest(
            Name: "get-test-namespace.servicebus.windows.net",
            ConnectionString: "Endpoint=sb://get-test.servicebus.windows.net/;SharedAccessKey=key==",
            AuthType: ConnectionAuthType.ConnectionString);

        var createResponse = await _client.PostAsJsonAsync(BaseUrl, createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>();

        var response = await _client.GetAsync($"{BaseUrl}/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await response.Content.ReadFromJsonAsync<NamespaceResponse>();
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
        var createRequest = new CreateNamespaceRequest(
            Name: "delete-test-namespace.servicebus.windows.net",
            ConnectionString: "Endpoint=sb://delete-test.servicebus.windows.net/;SharedAccessKey=key==",
            AuthType: ConnectionAuthType.ConnectionString);

        var createResponse = await _client.PostAsJsonAsync(BaseUrl, createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>();

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
