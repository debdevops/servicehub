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
            Intent: IntentHeaders.IntentPurgeMessage) };

        yield return new object[] { new RouteCase("Queue listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues") };

        yield return new object[] { new RouteCase("Topic listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics") };

        yield return new object[] { new RouteCase("Subscription listing", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/subscriptions") };

        yield return new object[] { new RouteCase("Namespace detail", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}") };

        // ── Routes added when closing the third cross-tenant exposure (GetScheduledMessages) ──

        yield return new object[] { new RouteCase("Queue scheduled messages", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/scheduled") };

        yield return new object[] { new RouteCase("Queue detail", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}") };

        yield return new object[] { new RouteCase("Queue send message", HttpMethod.Post,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/messages",
            Intent: IntentHeaders.IntentSendMessage,
            Body: new SendMessageRequest(EntityName: QueueName, Body: "test-body")) };

        yield return new object[] { new RouteCase("Queue dead-letter", HttpMethod.Post,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/deadletter",
            Intent: IntentHeaders.IntentDeadLetter) };

        yield return new object[] { new RouteCase("Queue cancel scheduled message", HttpMethod.Delete,
            id => $"/api/v1/namespaces/{id}/queues/{QueueName}/scheduled/1",
            Intent: IntentHeaders.IntentCancelScheduled) };

        yield return new object[] { new RouteCase("Topic detail", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}") };

        yield return new object[] { new RouteCase("Topic send message", HttpMethod.Post,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/messages",
            Intent: IntentHeaders.IntentSendMessage,
            Body: new SendMessageRequest(EntityName: TopicName, Body: "test-body")) };

        yield return new object[] { new RouteCase("Topic subscription dead-letter", HttpMethod.Post,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/subscriptions/{SubscriptionName}/deadletter",
            Intent: IntentHeaders.IntentDeadLetter) };

        yield return new object[] { new RouteCase("Subscription detail", HttpMethod.Get,
            id => $"/api/v1/namespaces/{id}/topics/{TopicName}/subscriptions/{SubscriptionName}") };

        yield return new object[] { new RouteCase("Message subscription peek", HttpMethod.Get,
            id => $"/api/v1/messages/topic/{TopicName}/subscription/{SubscriptionName}?namespaceId={id}") };

        yield return new object[] { new RouteCase("Message queue dead-letter peek", HttpMethod.Get,
            id => $"/api/v1/messages/queue/{QueueName}/deadletter?namespaceId={id}") };

        yield return new object[] { new RouteCase("Message subscription dead-letter peek", HttpMethod.Get,
            id => $"/api/v1/messages/topic/{TopicName}/subscription/{SubscriptionName}/deadletter?namespaceId={id}") };

        yield return new object[] { new RouteCase("Message replay", HttpMethod.Post,
            id => $"/api/v1/messages/replay?namespaceId={id}&sequenceNumber=1&entityName={QueueName}",
            Intent: IntentHeaders.IntentReplayMessage) };
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
        if (testCase.Intent is not null)
        {
            request.Headers.Add(IntentHeaders.IntentHeaderName, testCase.Intent);
            request.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        }

        if (testCase.Body is not null)
        {
            request.Content = JsonContent.Create(testCase.Body);
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

    public sealed record RouteCase(string Name, HttpMethod Method, Func<Guid, string> BuildPath, string? Intent = null, object? Body = null)
    {
        public override string ToString() => Name;
    }
}
