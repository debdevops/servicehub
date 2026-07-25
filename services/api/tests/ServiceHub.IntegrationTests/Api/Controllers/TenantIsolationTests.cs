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

/// <summary>
/// Executable specification of tenant isolation: for every namespace-scoped route in this table,
/// an owner who does not own the namespace must get 404, and the true owner must not. The second
/// assertion is essential — it catches a check that fails closed for everyone, which would pass a
/// "returns 404 for the wrong owner" test while being just as broken as no check at all.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<TenantIsolationWebApplicationFactory>
{
    private const string QueueName = "test-queue";
    private const string TopicName = "test-topic";
    private const string SubscriptionName = "test-sub";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly TenantIsolationWebApplicationFactory _factory;

    public TenantIsolationTests(TenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> Routes()
    {
        yield return new object[] { new RouteCase("Queue peek (active)", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/messages?queueType=active") };

        yield return new object[] { new RouteCase("Queue peek (deadletter)", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/messages?queueType=deadletter") };

        yield return new object[] { new RouteCase("Subscription peek", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/subscriptions/{SubscriptionName}/messages") };

        yield return new object[] { new RouteCase("Message detail (query-bound namespaceId)", HttpMethod.Get,
            id => $"/api/v1/messages/queue/{QueueName}?namespaceId={id}") };

        yield return new object[] { new RouteCase("Message purge", HttpMethod.Delete,
            id => $"/api/v1/messages/purge?namespaceId={id}&sequenceNumber=1&entityName={QueueName}",
            RequiresIntentHeaders: true) };

        yield return new object[] { new RouteCase("Queue listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues") };

        yield return new object[] { new RouteCase("Topic listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics") };

        yield return new object[] { new RouteCase("Subscription listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/subscriptions") };

        yield return new object[] { new RouteCase("Namespace detail", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}") };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_EnforcesTenantIsolation(RouteCase testCase)
    {
        using var ownerAClient = _factory.CreateOwnerAClient();
        using var ownerBClient = _factory.CreateOwnerBClient();

        var namespaceId = await CreateNamespaceAsOwnerAAsync(ownerAClient);

        var responseAsOwnerB = await SendAsync(ownerBClient, testCase, namespaceId);
        responseAsOwnerB.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            because: $"{testCase.Name}: owner B does not own this namespace and must not be able to read it");

        var responseAsOwnerA = await SendAsync(ownerAClient, testCase, namespaceId);
        responseAsOwnerA.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            because: $"{testCase.Name}: owner A owns this namespace — a 404 here means the check fails closed for everyone, not just intruders");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, RouteCase testCase, Guid namespaceId)
    {
        var request = new HttpRequestMessage(testCase.Method, testCase.BuildPath(namespaceId));
        if (testCase.RequiresIntentHeaders)
        {
            request.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentPurgeMessage);
            request.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        }

        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateNamespaceAsOwnerAAsync(HttpClient ownerAClient)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var request = new CreateNamespaceRequest(
            Name: $"tenant-iso-{unique}.servicebus.windows.net",
            ConnectionString: $"Endpoint=sb://tenant-iso-{unique}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString);

        var createResponse = await ownerAClient.PostAsJsonAsync("/api/v1/namespaces", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "namespace seeding must succeed for the test to be meaningful");

        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        return created!.Id;
    }

    public sealed record RouteCase(string Name, HttpMethod Method, Func<Guid, string> BuildPath, bool RequiresIntentHeaders = false)
    {
        public override string ToString() => Name;
    }
}
