using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Unit 2.3's list, through the real host: filtered, paged, newest first, scoped to one cloud, and
/// honest about what its groups add up to.
/// </summary>
public sealed class DeadLettersApiTests
{
    private sealed class Handle(ServiceHubApiFactory root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : IDisposable
    {
        public HttpClient Client { get; } = factory.CreateClient();
        public IServiceProvider Services => factory.Services;

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
            root.Dispose();
        }
    }

    private static Handle Host()
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, new PeekLog()));
            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Aws, ProviderCapabilities.Aws, new PeekLog()));
        }));
        return new Handle(root, factory);
    }

    private static async Task<Guid> Connect(HttpClient client, string provider)
    {
        object body = provider == "azure"
            ? new { name = "orders-dev", provider, authType = "connectionString", connectionString = "Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=secret==" }
            : new { name = "sqs.us-east-1.amazonaws.com", provider, authType = "awsAccessKey", awsRegion = "us-east-1", connectionString = "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY" };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/namespaces") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-ServiceHub-Intent", "create-namespace");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task Seed(Handle host, Guid namespaceId, CloudProviderType provider, int count, string? reason = "MaxDeliveryCountExceeded",
        string entity = "orders", TimeSpan? age = null, DlqMessageStatus status = DlqMessageStatus.Active, string prefix = "m")
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var owner = (await db.Namespaces.FindAsync(namespaceId))?.OwnerId ?? "someone-else";
        for (var i = 0; i < count; i++)
        {
            var seen = Now - (age ?? TimeSpan.Zero) - TimeSpan.FromMinutes(i);
            db.DlqMessages.Add(new DlqMessage
            {
                MessageId = $"{prefix}-{entity}-{reason}-{i}", SequenceNumber = Random.Shared.NextInt64(), BodyHash = "empty",
                NamespaceId = namespaceId, CloudProvider = provider, OwnerId = owner, EntityName = entity, EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = seen, DetectedAtUtc = seen, DeadLetterReason = reason, DeliveryCount = 5, MessageSize = 2048,
                BodyPreview = "SECRET-BODY-TEXT", Status = status,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Get(Handle host, string query, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.Client.GetAsync("/api/v1/dead-letters" + query);
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task A_request_must_say_which_dead_letters()
    {
        using var host = Host();
        var problem = await Get(host, "", HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Newest_first_and_paged_with_the_total_of_everything()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 30);

        var first = await Get(host, $"?namespaceId={ns}&pageSize=10");
        var second = await Get(host, $"?namespaceId={ns}&pageSize=10&page=2");

        first.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(30);
        first.GetProperty("items").GetArrayLength().Should().Be(10);
        var times = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("detectedAtUtc").GetDateTimeOffset()).ToList();
        times.Should().BeInDescendingOrder();
        first.GetProperty("items")[0].GetProperty("messageId").GetString().Should().NotBe(second.GetProperty("items")[0].GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task Page_size_is_capped_so_nothing_is_unbounded()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Get(host, $"?namespaceId={ns}&pageSize=101", HttpStatusCode.BadRequest);
        await Get(host, $"?namespaceId={ns}&page=0", HttpStatusCode.BadRequest);
        (await Get(host, $"?namespaceId={ns}")).GetProperty("paging").GetProperty("pageSize").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task A_cloud_is_never_mixed_with_another()
    {
        using var host = Host();
        var azure = await Connect(host.Client, "azure");
        var aws = await Connect(host.Client, "aws");
        await Seed(host, azure, CloudProviderType.Azure, 3);
        await Seed(host, aws, CloudProviderType.Aws, 5);

        (await Get(host, "?provider=Azure")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(3);
        (await Get(host, "?provider=Aws")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Rows_of_a_namespace_the_caller_does_not_have_are_never_returned_and_are_reported_as_not_found()
    {
        using var host = Host();
        var azure = await Connect(host.Client, "azure");
        var stranger = Guid.NewGuid();
        await Seed(host, stranger, CloudProviderType.Azure, 4);
        await Seed(host, azure, CloudProviderType.Azure, 2);

        (await Get(host, "?provider=Azure")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(2);
        await Get(host, $"?namespaceId={stranger}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reason_chips_add_up_to_the_set_and_choosing_one_does_not_hide_the_others()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 5, "ValidationFailed");
        await Seed(host, ns, CloudProviderType.Azure, 3, "TimedOut");
        await Seed(host, ns, CloudProviderType.Azure, 2, reason: null);

        var all = await Get(host, $"?namespaceId={ns}");
        var picked = await Get(host, $"?namespaceId={ns}&reason=ValidationFailed");
        var none = await Get(host, $"?namespaceId={ns}&noReason=true");

        int Sum(JsonElement r) => r.GetProperty("groups").EnumerateArray().Sum(g => g.GetProperty("count").GetInt32());
        all.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(10);
        Sum(all).Should().Be(10);
        all.GetProperty("groups")[0].GetProperty("reason").GetString().Should().Be("ValidationFailed", "largest first");

        picked.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(5, "the pager counts the filtered set");
        Sum(picked).Should().Be(10, "the chips still count the whole tab");
        none.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(2);
        all.GetProperty("groups").EnumerateArray().Should().Contain(g => g.GetProperty("reason").ValueKind == JsonValueKind.Null, "no reason is a group of its own, said plainly");
    }

    [Fact]
    public async Task Rare_reasons_fold_into_other_and_the_counts_still_add_up()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        for (var i = 0; i < 12; i++)
        {
            await Seed(host, ns, CloudProviderType.Azure, 12 - i, $"Reason{i:00}");
        }

        var page = await Get(host, $"?namespaceId={ns}");

        page.GetProperty("groups").GetArrayLength().Should().Be(8);
        var other = page.GetProperty("otherReasons");
        other.GetProperty("kinds").GetInt32().Should().Be(4);
        (page.GetProperty("groups").EnumerateArray().Sum(g => g.GetProperty("count").GetInt32()) + other.GetProperty("count").GetInt32())
            .Should().Be(page.GetProperty("paging").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Resolved_messages_are_not_dead_letters_unless_asked_for()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 3);
        await Seed(host, ns, CloudProviderType.Azure, 2, status: DlqMessageStatus.Resolved, prefix: "gone");

        (await Get(host, $"?namespaceId={ns}")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(3);
        (await Get(host, $"?namespaceId={ns}&status=resolved")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(2);
        (await Get(host, $"?namespaceId={ns}&status=all")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(5);
        await Get(host, $"?namespaceId={ns}&status=nonsense", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_window_filters_by_when_a_message_was_first_seen_and_defaults_to_all_time()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 2, prefix: "fresh");
        await Seed(host, ns, CloudProviderType.Azure, 3, age: TimeSpan.FromDays(3), prefix: "old");

        (await Get(host, $"?namespaceId={ns}")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(5, "an old dead letter is still stuck — it must not vanish from the default view");
        (await Get(host, $"?namespaceId={ns}&range=24h")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(2);
        (await Get(host, $"?namespaceId={ns}&range=7d")).GetProperty("paging").GetProperty("total").GetInt32().Should().Be(5);
        await Get(host, $"?namespaceId={ns}&range=forever", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_finds_ids_entities_and_reasons_treats_percent_literally_and_never_looks_in_the_body()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 2, "Alpha", "orders");
        await Seed(host, ns, CloudProviderType.Azure, 3, "Beta", "billing");
        await Seed(host, ns, CloudProviderType.Azure, 1, "100%_done", "misc");

        async Task<int> Count(string q) =>
            (await Get(host, $"?namespaceId={ns}&q={Uri.EscapeDataString(q)}")).GetProperty("paging").GetProperty("total").GetInt32();

        (await Count("billing")).Should().Be(3, "entity");
        (await Count("alph")).Should().Be(2, "reason, case-insensitively");
        (await Count("m-orders")).Should().Be(2, "message id");
        (await Count("%")).Should().Be(1, "a percent sign is a percent sign, not a wildcard");
        (await Count("SECRET-BODY-TEXT")).Should().Be(0, "bodies are not searchable");
    }

    [Fact]
    public async Task The_entity_filter_and_its_list_of_choices_work()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 2, entity: "orders");
        await Seed(host, ns, CloudProviderType.Azure, 4, entity: "billing");

        var only = await Get(host, $"?namespaceId={ns}&entity=billing");

        only.GetProperty("paging").GetProperty("total").GetInt32().Should().Be(4);
        only.GetProperty("entities").EnumerateArray().Select(e => e.GetString()).Should().Equal("billing", "orders");
    }

    [Fact]
    public async Task A_list_carries_no_message_body()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 1);

        var response = await host.Client.GetAsync($"/api/v1/dead-letters?namespaceId={ns}");

        (await response.Content.ReadAsStringAsync()).Should().NotContain("SECRET-BODY-TEXT").And.NotContain("bodyPreview");
    }
}
