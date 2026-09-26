using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// <c>POST /namespaces/{id}/dead-letters/look</c>: how AWS and Google Cloud dead letters reach the list. The
/// automatic monitor never looks there (a look is a delivery attempt); a person asking is the consent — and
/// what is seen is recorded, so it can be opened and replayed like any other dead letter.
/// </summary>
public sealed class DeadLetterLookApiTests
{
    private sealed class Handle(ServiceHubApiFactory root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient client, LookableCloud cloud) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public LookableCloud Cloud { get; } = cloud;

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
            root.Dispose();
        }
    }

    private static Handle Host()
    {
        var cloud = new LookableCloud();
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(cloud);
        }));
        return new Handle(root, factory, factory.CreateClient(), cloud);
    }

    private static async Task<Guid> ConnectAwsAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/namespaces")
        {
            Content = JsonContent.Create(new
            {
                name = "sqs.us-east-1.amazonaws.com", provider = "aws", authType = "awsAccessKey", awsRegion = "us-east-1",
                connectionString = "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            }),
        };
        request.Headers.Add("X-ServiceHub-Intent", "create-namespace");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Look(HttpClient client, Guid id, bool withIntent = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/namespaces/{id}/dead-letters/look");
        if (withIntent)
        {
            request.Headers.Add("X-ServiceHub-Intent", "look-at-dead-letters");
        }

        return client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Without_the_intent_header_nothing_is_looked_at()
    {
        using var h = Host();
        var id = await ConnectAwsAsync(h.Client);
        h.Cloud.ResetPeeks();

        var response = await Look(h.Client, id, withIntent: false);

        response.StatusCode.Should().Be((HttpStatusCode)428);
        (await Json(response)).GetProperty("code").GetString().Should().Be("intent_required");
        h.Cloud.Peeks.Should().Be(0, "a look on this cloud is a delivery attempt — never from a stray request");
    }

    [Fact]
    public async Task A_look_records_what_it_sees_so_the_list_shows_it_and_says_it_was_a_delivery_attempt()
    {
        using var h = Host();
        var id = await ConnectAwsAsync(h.Client);

        (await Json(await h.Client.GetAsync("/api/v1/dead-letters?provider=aws"))).GetProperty("paging").GetProperty("total").GetInt32()
            .Should().Be(0, "the automatic monitor never looks at a cloud without a repeatable peek");

        var response = await Look(h.Client, id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("outcome").GetString().Should().Be("looked");
        body.GetProperty("newMessages").GetInt32().Should().Be(3);
        body.GetProperty("queuesExamined").GetInt32().Should().Be(1);
        body.GetProperty("countsAsDeliveryAttempt").GetBoolean().Should().BeTrue();

        var list = await Json(await h.Client.GetAsync("/api/v1/dead-letters?provider=aws"));
        list.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(3);

        // The row is a full dead letter: its detail carries the body, so it can be read and replayed.
        var first = list.GetProperty("items")[0].GetProperty("id").GetInt64();
        var detail = await Json(await h.Client.GetAsync($"/api/v1/dead-letters/{first}"));
        detail.GetProperty("bodyPreview").GetString().Should().Contain("customerId");

        // Looking again records nothing twice.
        (await Json(await Look(h.Client, id))).GetProperty("newMessages").GetInt32().Should().Be(0);
        (await Json(await h.Client.GetAsync("/api/v1/dead-letters?provider=aws"))).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_look_is_on_the_audit_trail()
    {
        using var h = Host();
        var id = await ConnectAwsAsync(h.Client);
        await Look(h.Client, id);

        var audit = await Json(await h.Client.GetAsync("/api/v1/audit?action=DeadLetters.Look"));
        audit.GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("outcome").GetString() == "Success");
    }

    [Fact]
    public async Task A_cloud_that_cannot_be_read_is_an_answer_not_an_error_and_changes_nothing()
    {
        using var h = Host();
        var id = await ConnectAwsAsync(h.Client);
        h.Cloud.ListingFails = true;

        var response = await Look(h.Client, id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("outcome").GetString().Should().Be("failed");
        body.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Someone_elses_or_a_missing_namespace_is_a_404()
    {
        using var h = Host();
        (await Look(h.Client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>An AWS-like cloud (no repeatable peek) with three dead letters in "orders" that record no reason.</summary>
internal sealed class LookableCloud : ICloudMessagingProvider, IMessageReceiver
{
    private int _peeks;
    public int Peeks => _peeks;
    public void ResetPeeks() => Interlocked.Exchange(ref _peeks, 0);
    public bool ListingFails { get; set; }

    public CloudProviderType ProviderType => CloudProviderType.Aws;
    public ProviderCapabilities Capabilities => ProviderCapabilities.Aws;
    public IMessageReceiver GetMessageReceiver() => this;
    public IMessageSender GetMessageSender() => throw new NotSupportedException();
    public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Result.Success());

    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) =>
        Task.FromResult(ListingFails
            ? Result.Failure<IReadOnlyList<CloudEntity>>(Error.ExternalService("fake.list", "the cloud did not answer"))
            : Result.Success<IReadOnlyList<CloudEntity>>([new CloudEntity { Name = "orders", EntityType = "Queue", DeadLetterCount = 3, Provider = CloudProviderType.Aws }]));

    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _peeks);
        IReadOnlyList<Message> page =
        [
            .. Enumerable.Range(1, 3).Select(i => new Message
            {
                MessageId = $"sqs-{i}", SequenceNumber = 1000 + i, IsFromDeadLetter = true, DeliveryCount = 5,
                Body = $"{{\"orderId\":{i},\"error\":\"Required field customerId missing\"}}",
                EnqueuedTime = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero),
            }),
        ];
        return Task.FromResult(Result.Success(page));
    }

    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<long>> GetMessageCountAsync(Guid namespaceId, string entityName, string? subscriptionName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<bool>> ReplayMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, string? recoveryMarker, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result> PurgeMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
