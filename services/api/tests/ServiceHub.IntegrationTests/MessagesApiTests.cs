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
/// Unit 2.2 through the real host, with the clouds replaced by fakes: both peeks, paging, the single
/// message, and above all that "is it safe to look again?" comes from capabilities, not a provider name.
/// </summary>
public sealed class MessagesApiTests
{
    private sealed class Handle(ServiceHubApiFactory root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient client, PeekLog log) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public PeekLog Log { get; } = log;

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
            root.Dispose();
        }
    }

    private static Handle Host()
    {
        var log = new PeekLog();
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, log));
            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Aws, ProviderCapabilities.Aws, log));
        }));
        return new Handle(root, factory, factory.CreateClient(), log);
    }

    private static async Task<Guid> ConnectAsync(HttpClient client, string provider)
    {
        object body = provider == "azure"
            ? new
            {
                name = "orders-dev", provider, authType = "connectionString",
                connectionString = "Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=secret==",
            }
            : new
            {
                name = "sqs.us-east-1.amazonaws.com", provider, authType = "awsAccessKey", awsRegion = "us-east-1",
                connectionString = "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/namespaces") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-ServiceHub-Intent", "create-namespace");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    // ── Both sides, every provider ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("azure", "messages/peek", false)]
    [InlineData("azure", "dead-letter/peek", true)]
    [InlineData("aws", "messages/peek", false)]
    [InlineData("aws", "dead-letter/peek", true)]
    public async Task Both_peeks_work_on_every_provider(string provider, string path, bool deadLetter)
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, provider);

        var response = await host.Client.GetAsync($"/api/v1/namespaces/{id}/{path}?entity=orders&max=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json.GetProperty("deadLetter").GetBoolean().Should().Be(deadLetter);
        json.GetProperty("messages").GetArrayLength().Should().Be(5);
        json.GetProperty("messages")[0].GetProperty("isFromDeadLetter").GetBoolean().Should().Be(deadLetter);
        json.GetProperty("messages")[0].GetProperty("body").GetString().Should().NotBeNullOrEmpty();
    }

    // ── Paging: the API pages, so the UI pages ──────────────────────────────────────────────────

    [Fact]
    public async Task A_repeatable_cloud_pages_with_a_cursor_that_walks_the_whole_queue()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        var seen = new List<long>();
        long? from = null;
        for (var page = 0; page < 10; page++)
        {
            var url = $"/api/v1/namespaces/{id}/messages/peek?entity=orders&max=10" + (from is null ? "" : $"&from={from}");
            var json = await Json(await host.Client.GetAsync(url));
            seen.AddRange(json.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("sequenceNumber").GetInt64()));

            var next = json.GetProperty("paging").GetProperty("nextFromSequenceNumber");
            if (next.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            from = next.GetInt64();
        }

        seen.Should().Equal(Enumerable.Range(1, PeekableProvider.Total).Select(i => (long)i));
    }

    [Fact]
    public async Task No_endpoint_is_unbounded_a_page_is_capped_and_defaults_small()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        var defaulted = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/peek?entity=orders"));
        defaulted.GetProperty("paging").GetProperty("requested").GetInt32().Should().Be(25);

        (await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/peek?entity=orders&max=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/peek?entity=orders&max=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── The capability, not the provider name ───────────────────────────────────────────────────

    [Fact]
    public async Task The_response_says_whether_peeking_is_safe_and_it_differs_because_of_the_capability()
    {
        using var host = Host();
        var azure = await ConnectAsync(host.Client, "azure");
        var aws = await ConnectAsync(host.Client, "aws");

        var safe = (await Json(await host.Client.GetAsync($"/api/v1/namespaces/{azure}/dead-letter/peek?entity=orders&max=3"))).GetProperty("peek");
        var unsafePeek = (await Json(await host.Client.GetAsync($"/api/v1/namespaces/{aws}/dead-letter/peek?entity=orders&max=3"))).GetProperty("peek");

        safe.GetProperty("repeatable").GetBoolean().Should().BeTrue();
        safe.GetProperty("warning").ValueKind.Should().Be(JsonValueKind.Null);
        unsafePeek.GetProperty("repeatable").GetBoolean().Should().BeFalse();
        unsafePeek.GetProperty("warning").GetString().Should().Contain("delivery attempt");
    }

    [Fact]
    public async Task A_cloud_that_cannot_page_gives_no_cursor_and_refuses_one()
    {
        using var host = Host();
        var aws = await ConnectAsync(host.Client, "aws");

        var page = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{aws}/messages/peek?entity=orders&max=10"));
        page.GetProperty("paging").GetProperty("returned").GetInt32().Should().Be(10);
        page.GetProperty("paging").GetProperty("nextFromSequenceNumber").ValueKind.Should().Be(JsonValueKind.Null, "a full page proves nothing about more where there is no cursor");

        var withCursor = await host.Client.GetAsync($"/api/v1/namespaces/{aws}/messages/peek?entity=orders&from=5");
        withCursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(withCursor)).GetProperty("detail").GetString().Should().Contain("cannot page");
    }

    // ── One message ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_message_is_found_by_its_sequence_number_and_a_missing_one_is_a_404()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        var found = await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/7?entity=orders&deadLetter=true");
        found.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(found)).GetProperty("sequenceNumber").GetInt64().Should().Be(7);

        var missing = await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/9999?entity=orders");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Looking_up_one_message_never_peeks_where_peeking_is_a_delivery_attempt()
    {
        using var host = Host();
        var aws = await ConnectAsync(host.Client, "aws");
        host.Log.Reset();

        var response = await host.Client.GetAsync($"/api/v1/namespaces/{aws}/messages/7?entity=orders");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response)).GetProperty("code").GetString().Should().Be("capability_unavailable");
        host.Log.Calls.Should().Be(0, "the refusal happens before the cloud is touched");
    }

    // ── Scoping and validation ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_namespace_and_a_missing_entity_are_plain_errors()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        (await host.Client.GetAsync($"/api/v1/namespaces/{Guid.NewGuid()}/messages/peek?entity=orders")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var noEntity = await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/peek");
        noEntity.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(noEntity)).GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task A_provider_failure_is_a_502_with_a_sentence_not_a_stack_trace()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        var response = await host.Client.GetAsync($"/api/v1/namespaces/{id}/messages/peek?entity=broken");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("   at ");
    }

    [Fact]
    public async Task There_is_no_way_to_change_a_message_through_this_controller()
    {
        using var host = Host();
        var id = await ConnectAsync(host.Client, "azure");

        foreach (var method in new[] { HttpMethod.Delete, HttpMethod.Post, HttpMethod.Put })
        {
            var response = await host.Client.SendAsync(new HttpRequestMessage(method, $"/api/v1/namespaces/{id}/messages/7?entity=orders"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        }
    }
}

internal sealed class PeekLog
{
    private int _calls;
    public int Calls => _calls;
    public void Hit() => Interlocked.Increment(ref _calls);
    public void Reset() => Interlocked.Exchange(ref _calls, 0);

    /// <summary>What a replay does when a test asks for one. Null keeps the old behaviour: not supported.</summary>
    public Func<Result<bool>>? OnReplay { get; set; }

    private int _replays;
    public int Replays => _replays;
    public void HitReplay() => Interlocked.Increment(ref _replays);

    /// <summary>The queue-or-topic and subscription the last replay asked the cloud for.</summary>
    public (string Entity, string? Subscription)? LastReplayTarget { get; set; }
}

/// <summary>A cloud with 30 active and 30 dead-lettered messages in "orders", and a "broken" entity that fails.</summary>
internal sealed class PeekableProvider(CloudProviderType type, ProviderCapabilities capabilities, PeekLog log) : ICloudMessagingProvider, IMessageReceiver
{
    public const int Total = 30;

    public CloudProviderType ProviderType => type;
    public ProviderCapabilities Capabilities => capabilities;
    public IMessageReceiver GetMessageReceiver() => this;
    public IMessageSender GetMessageSender() => throw new NotSupportedException();
    public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Result.Success());
    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) =>
        Task.FromResult(Result.Success<IReadOnlyList<CloudEntity>>([]));

    private Task<Result<IReadOnlyList<Message>>> Peek(GetMessagesRequest r, bool dead)
    {
        log.Hit();
        if (r.EntityName == "broken")
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("fake.peek", "the cloud did not answer")));
        }

        var from = r.FromSequenceNumber ?? 0;
        IReadOnlyList<Message> page =
        [
            .. Enumerable.Range(1, Total)
                .Where(i => i >= from)
                .Take(r.MaxMessages)
                .Select(i => new Message
                {
                    MessageId = $"m-{i}", SequenceNumber = i, Body = $"{{\"order\":{i}}}", IsFromDeadLetter = dead,
                    EnqueuedTime = new DateTimeOffset(2026, 9, 24, 13, 0, 0, TimeSpan.Zero), DeliveryCount = dead ? 10 : 1,
                    DeadLetterReason = dead ? "MaxDeliveryCountExceeded" : null,
                }),
        ];
        return Task.FromResult(Result.Success(page));
    }

    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default) => Peek(request, false);
    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default) => Peek(request, true);
    public Task<Result<long>> GetMessageCountAsync(Guid namespaceId, string entityName, string? subscriptionName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<bool>> ReplayMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, string? recoveryMarker, CancellationToken cancellationToken = default)
    {
        log.HitReplay();
        log.LastReplayTarget = (entityName, subscriptionName);
        return log.OnReplay is { } replay ? Task.FromResult(replay()) : throw new NotSupportedException();
    }
    public Task<Result> PurgeMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
