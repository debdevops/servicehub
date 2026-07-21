using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ServiceHub.Api.Security;
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

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{created!.Id}");
        deleteRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentDeleteNamespace);
        deleteRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        var response = await _client.SendAsync(deleteRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{nonExistentId}");
        deleteRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentDeleteNamespace);
        deleteRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        var response = await _client.SendAsync(deleteRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Share / RevokeShare ──────────────────────────────────────────────────
    // Authentication is disabled in this test host (every request resolves to the same SPA
    // owner identity), so these tests exercise the API contract and persistence — full pipeline
    // request routing, intent-header enforcement, response shape. Cross-identity access
    // (does a namespace shared with owner B actually become visible to B?) is covered by
    // ApiControllerBaseTests' GetOwnedNamespaceAsync unit tests and verified live against two
    // real scoped API keys as part of Docker/Simulator verification.

    [Fact]
    public async Task Share_MissingIntentHeaders_ShouldReturn428()
    {
        var request = MakeRequest("sharenointent");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"{BaseUrl}/{created!.Id}/share", new ShareNamespaceRequest("colleague-owner"));

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task Share_ThenGetById_ReflectsSharedOwnerInResponse()
    {
        var request = MakeRequest("shareok");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{created!.Id}/share")
        {
            Content = JsonContent.Create(new ShareNamespaceRequest("colleague-owner-123"))
        };
        shareRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentShareNamespace);
        shareRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        var shareResponse = await _client.SendAsync(shareRequest);

        shareResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var shared = await shareResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        shared!.SharedWithOwnerIds.Should().Contain("colleague-owner-123");

        var getResponse = await _client.GetAsync($"{BaseUrl}/{created.Id}");
        var refetched = await getResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        refetched!.SharedWithOwnerIds.Should().Contain("colleague-owner-123");
    }

    [Fact]
    public async Task Share_WithSelf_ShouldReturnBadRequest()
    {
        var request = MakeRequest("shareself");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{created!.Id}/share")
        {
            Content = JsonContent.Create(new ShareNamespaceRequest("__spa__"))
        };
        shareRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentShareNamespace);
        shareRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        var response = await _client.SendAsync(shareRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShareThenRevoke_RemovesSharedOwnerFromResponse()
    {
        var request = MakeRequest("sharerevoke");
        var createResponse = await _client.PostAsJsonAsync(BaseUrl, request);
        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);

        var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{created!.Id}/share")
        {
            Content = JsonContent.Create(new ShareNamespaceRequest("temp-owner"))
        };
        shareRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentShareNamespace);
        shareRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        await _client.SendAsync(shareRequest);

        var revokeRequest = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{created.Id}/share/temp-owner");
        revokeRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentShareNamespace);
        revokeRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        var revokeResponse = await _client.SendAsync(revokeRequest);

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"{BaseUrl}/{created.Id}");
        var refetched = await getResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        refetched!.SharedWithOwnerIds.Should().NotContain("temp-owner");
    }

    [Fact]
    public async Task Share_NonExistentNamespace_ShouldReturnNotFound()
    {
        var shareRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{Guid.NewGuid()}/share")
        {
            Content = JsonContent.Create(new ShareNamespaceRequest("colleague-owner"))
        };
        shareRequest.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentShareNamespace);
        shareRequest.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");

        var response = await _client.SendAsync(shareRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── /api/v1/me ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_ReturnsSpaOwnerIdentityByDefault()
    {
        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        me!.OwnerId.Should().Be("__spa__");
    }
}
