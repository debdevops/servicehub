using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Unit 2.3's list, through the real host: filtered, paged, newest first, scoped to one cloud, and
/// honest about what its groups add up to.
/// </summary>
public sealed class DeadLettersApiTests
{
    internal sealed class Handle(ServiceHubApiFactory root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : IDisposable
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

    internal static Handle Host() => Host(new PeekLog());

    internal static Handle Host(PeekLog azure, PeekLog? aws = null)
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, azure));
            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Aws, ProviderCapabilities.Aws, aws ?? new PeekLog()));
        }));
        return new Handle(root, factory);
    }

    internal static async Task<Guid> Connect(HttpClient client, string provider)
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

    internal static async Task Seed(Handle host, Guid namespaceId, CloudProviderType provider, int count, string? reason = "MaxDeliveryCountExceeded",
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

    [Fact]
    public async Task One_dead_letter_opens_with_its_stored_body_preview()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 3);
        var id = (await Get(host, $"?namespaceId={ns}")).GetProperty("items")[0].GetProperty("id").GetInt64();

        var response = await host.Client.GetAsync($"/api/v1/dead-letters/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("bodyPreview").GetString().Should().Be("SECRET-BODY-TEXT");
        doc.GetProperty("bodyIsPreview").GetBoolean().Should().BeTrue();
        doc.GetProperty("othersLikeIt").GetInt32().Should().Be(2, "two other active dead letters in this queue share the reason");
        doc.GetProperty("item").GetProperty("id").GetInt64().Should().Be(id);
    }

    [Fact]
    public async Task A_dead_letter_that_is_not_there_is_a_404()
    {
        using var host = Host();
        await Connect(host.Client, "azure");

        var response = await host.Client.GetAsync("/api/v1/dead-letters/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_owners_dead_letter_looks_exactly_like_one_that_is_not_there()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        var stranger = Guid.NewGuid();
        await Seed(host, stranger, CloudProviderType.Azure, 1, prefix: "theirs");
        long id;
        using (var scope = host.Services.CreateScope())
        {
            id = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Single(m => m.NamespaceId == stranger).Id;
        }

        var response = await host.Client.GetAsync($"/api/v1/dead-letters/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ns.Should().NotBe(Guid.Empty);
    }

    private static async Task<JsonElement> Eligibility(Handle host, long id, string query = "", HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.Client.GetAsync($"/api/v1/dead-letters/{id}/eligibility{query}");
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<long> FirstId(Handle host, Guid ns) =>
        (await Get(host, $"?namespaceId={ns}")).GetProperty("items")[0].GetProperty("id").GetInt64();

    [Fact]
    public async Task A_dev_replay_is_allowed_and_carries_no_reason()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 1);

        var decision = await Eligibility(host, await FirstId(host, ns));

        decision.GetProperty("verdict").GetString().Should().Be("Allow");
        decision.GetProperty("reasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        decision.GetProperty("approvable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_prod_namespace_is_denied_with_its_reason_code_and_a_deny_is_not_approvable()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 1);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var row = await db.Namespaces.FindAsync(ns);
            typeof(Namespace).GetProperty(nameof(Namespace.Environment))!.SetValue(row, EnvironmentType.Prod);
            await db.SaveChangesAsync();
        }

        var decision = await Eligibility(host, await FirstId(host, ns));

        decision.GetProperty("verdict").GetString().Should().Be("Deny");
        decision.GetProperty("reasonCode").GetString().Should().Be("PRODUCTION_ELEVATION_REQUIRED");
        decision.GetProperty("approvable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_action_is_a_400_and_a_missing_message_a_404()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 1);

        await Eligibility(host, await FirstId(host, ns), "?action=explode", HttpStatusCode.BadRequest);
        await Eligibility(host, 999999, "", HttpStatusCode.NotFound);
    }

    // ── Unit 2.7: replay, proposal first ────────────────────────────────────────

    private static async Task<HttpResponseMessage> PostReplay(Handle host, long id, bool withIntent = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay");
        if (withIntent)
        {
            request.Headers.Add("X-ServiceHub-Intent", "replay-message");
        }

        return await host.Client.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<(Handle Host, PeekLog Log, Guid Ns, long Id)> Seeded(Func<Result<bool>>? replay)
    {
        var log = new PeekLog { OnReplay = replay };
        var host = Host(log);
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 1);
        return (host, log, ns, await FirstId(host, ns));
    }

    [Fact]
    public async Task The_proposal_says_what_will_happen_before_anything_runs()
    {
        var (host, log, _, id) = await Seeded(() => Result<bool>.Success(true));
        using var _ = host;

        var response = await host.Client.GetAsync($"/api/v1/dead-letters/{id}/replay-proposal");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await Body(response);
        proposal.GetProperty("canExecute").GetBoolean().Should().BeTrue();
        proposal.GetProperty("verdict").GetString().Should().Be("Allow");
        proposal.GetProperty("targetEntity").GetString().Should().Be("orders");
        proposal.GetProperty("canConfirm").GetBoolean().Should().BeTrue();
        proposal.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("id").GetString())
            .Should().Equal("status", "environment", "frequency", "verification");
        log.Replays.Should().Be(0, "a proposal must never touch the cloud");
    }

    [Fact]
    public async Task A_replay_without_the_intent_header_is_refused_and_touches_nothing()
    {
        var (host, log, _, id) = await Seeded(() => Result<bool>.Success(true));
        using var _ = host;

        var response = await PostReplay(host, id, withIntent: false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("intent_required");
        log.Replays.Should().Be(0);
    }

    [Fact]
    public async Task An_accepted_replay_is_in_the_ledger_and_the_history_and_the_chain_verifies()
    {
        var (host, log, ns, id) = await Seeded(() => Result<bool>.Success(true));
        using var _ = host;

        var response = await PostReplay(host, id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = await Body(response);
        outcome.GetProperty("result").GetString().Should().Be("accepted");
        outcome.GetProperty("state").GetString().Should().Be("Observing");
        outcome.GetProperty("markerApplied").GetBoolean().Should().BeTrue();
        log.Replays.Should().Be(1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
        (await db.DlqMessages.FindAsync(id))!.Status.Should().Be(DlqMessageStatus.Resolved);
        var entry = await db.RecoveryLedgerEntries.SingleAsync();
        entry.State.Should().Be(RecoveryEntryState.Observing);
        entry.RecoveryMarker.Should().Be(entry.Id.ToString());
        (await db.ReplayHistories.SingleAsync()).RecoveryEntryId.Should().Be(entry.Id);
        (await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>().VerifyChainAsync(owner)).IsValid.Should().BeTrue();

        var listed = await (await host.Client.GetAsync($"/api/v1/replays?namespaceId={ns}")).Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(listed).RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        // Nothing is configured, so the actor is a browser session — and the API says so instead of naming anyone.
        items[0].GetProperty("actor").GetProperty("isSession").GetBoolean().Should().BeTrue();
        items[0].GetProperty("actor").GetProperty("label").GetString().Should().Be("from this browser session");
    }

    [Fact]
    public async Task A_replay_the_cloud_refuses_is_recorded_as_failed_and_the_message_stays_put()
    {
        var (host, _, _, id) = await Seeded(() => Result<bool>.Failure(Error.NotFound("Message.NotFound", "That message is no longer in the dead-letter queue.")));
        using var _ = host;

        var outcome = await Body(await PostReplay(host, id));

        outcome.GetProperty("result").GetString().Should().Be("rejected");
        outcome.GetProperty("state").GetString().Should().Be("ExecutionFailed");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.DlqMessages.FindAsync(id))!.Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task A_replay_that_loses_contact_is_unknown_never_failed()
    {
        // A thrown exception, and an internal failure the router reports instead of throwing, are both "we do not know".
        foreach (Func<Result<bool>>? behaviour in new Func<Result<bool>>?[] { null, () => Result<bool>.Failure(Error.Internal("General.UnexpectedError", "boom")) })
        {
            var (host, _, _, id) = await Seeded(behaviour);
            using var _ = host;

            var outcome = await Body(await PostReplay(host, id));

            outcome.GetProperty("result").GetString().Should().Be("unknown");
            outcome.GetProperty("state").GetString().Should().Be("ExecutionUnknown");
        }
    }

    [Fact]
    public async Task A_prod_namespace_is_refused_by_the_gate_with_its_code_and_the_cloud_is_never_called()
    {
        var (host, log, ns, id) = await Seeded(() => Result<bool>.Success(true));
        using var _ = host;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            typeof(Namespace).GetProperty(nameof(Namespace.Environment))!.SetValue(await db.Namespaces.FindAsync(ns), EnvironmentType.Prod);
            await db.SaveChangesAsync();
        }

        var response = await PostReplay(host, id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("code").GetString().Should().Be("PRODUCTION_ELEVATION_REQUIRED");
        log.Replays.Should().Be(0);
        using var check = host.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<ServiceHubDbContext>().RecoveryLedgerEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_second_replay_of_the_same_message_is_refused()
    {
        var (host, log, _, id) = await Seeded(() => Result<bool>.Success(true));
        using var _ = host;
        (await PostReplay(host, id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await PostReplay(host, id);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body(again)).GetProperty("code").GetString().Should().Be("NOT_ACTIVE");
        log.Replays.Should().Be(1);
    }

    // ── Unit 2.8: the same replay, two clouds, two honest answers ───────────────────────────────

    [Fact]
    public async Task The_same_replay_on_azure_and_aws_is_visibly_different_and_driven_by_capability()
    {
        var azureLog = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        var awsLog = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using var host = Host(azureLog, awsLog);
        var azure = await Connect(host.Client, "azure");
        var aws = await Connect(host.Client, "aws");
        await Seed(host, azure, CloudProviderType.Azure, 1);
        await Seed(host, aws, CloudProviderType.Aws, 1);
        var azureId = await FirstId(host, azure);
        var awsId = await FirstId(host, aws);
        (await PostReplay(host, azureId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostReplay(host, awsId)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Both are being watched; nothing has been claimed yet.
        (await Verification(host, azureId)).GetProperty("status").GetString().Should().Be("watching");
        (await Verification(host, awsId)).GetProperty("status").GetString().Should().Be("watching");

        // Let both windows end, then let the verifier run.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            foreach (var entry in await db.RecoveryLedgerEntries.ToListAsync())
            {
                entry.ObservationWindowEndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            }

            await db.SaveChangesAsync();
        }

        var verifier = host.Services.GetServices<IAgent>().OfType<ServiceHub.Infrastructure.Agents.RecoveryVerificationAgent>().Single();
        await verifier.ExecuteCycleAsync(CancellationToken.None);

        var onAzure = await Verification(host, azureId);
        onAzure.GetProperty("status").GetString().Should().Be("verified");
        onAzure.GetProperty("canConfirm").GetBoolean().Should().BeTrue();

        var onAws = await Verification(host, awsId);
        onAws.GetProperty("status").GetString().Should().Be("verification_required");
        onAws.GetProperty("reasonCode").GetString().Should().Be("AWS_NO_ABSENCE_PROOF");
        onAws.GetProperty("remedy").GetString().Should().Be("SETUP_DLQ_OBSERVER");
        onAws.GetProperty("canConfirm").GetBoolean().Should().BeFalse();

        // 2.12: the summary matches a hand-count of what just happened — one proof, one hope, never merged.
        var summary = await Body(await host.Client.GetAsync("/api/v1/recovery/summary?window=24h"));
        summary.GetProperty("total").GetInt32().Should().Be(2);
        int Count(JsonElement states, string state) => states.EnumerateArray().Single(x => x.GetProperty("state").GetString() == state).GetProperty("count").GetInt32();
        Count(summary.GetProperty("states"), "Recovered").Should().Be(1);
        Count(summary.GetProperty("states"), "Unverified").Should().Be(1);
        Count(summary.GetProperty("states"), "Returned").Should().Be(0);
        summary.GetProperty("stayedFixedRate").GetDouble().Should().Be(1.0, "Unverified is on neither side of the rate");
        summary.GetProperty("byProvider").GetArrayLength().Should().Be(2);

        // 2.13: the list and one entry opened, filtered by state, and the chain verifies.
        var unverified = await Body(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&state=Unverified"));
        unverified.GetProperty("total").GetInt32().Should().Be(1);
        var entryId = unverified.GetProperty("items")[0].GetProperty("id").GetGuid();
        var detail = await Body(await host.Client.GetAsync($"/api/v1/recovery/entries/{entryId}"));
        detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("eventType").GetString())
            .Should().ContainInOrder("EntryBegun", "ProviderAccepted", "ObservationWindowOpened", "ObservationUnavailable");
        (await Body(await host.Client.GetAsync("/api/v1/recovery/chain"))).GetProperty("isValid").GetBoolean().Should().BeTrue();

        (await host.Client.GetAsync("/api/v1/recovery/summary?window=forever")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/recovery/entries?state=Nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync($"/api/v1/recovery/entries/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<JsonElement> Verification(Handle host, long dlqMessageId)
    {
        var page = await Body(await host.Client.GetAsync($"/api/v1/replays?dlqMessageId={dlqMessageId}"));
        return page.GetProperty("items")[0].GetProperty("verification");
    }

    // ── Unit 2.10: the trend ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_trend_has_every_day_zeros_included_and_counts_new_and_resolved_by_day()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");
        await Seed(host, ns, CloudProviderType.Azure, 3, age: TimeSpan.FromHours(1));                 // new today
        await Seed(host, ns, CloudProviderType.Azure, 2, age: TimeSpan.FromDays(3), prefix: "old");    // new 3 days ago
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var gone = await db.DlqMessages.Where(m => m.MessageId.StartsWith("old")).ToListAsync();
            foreach (var m in gone)
            {
                m.Status = DlqMessageStatus.Resolved;
                m.ResolvedAt = DateTimeOffset.UtcNow.AddHours(-1);
            }

            await db.SaveChangesAsync();
        }

        var response = await host.Client.GetAsync($"/api/v1/dead-letters/trend?namespaceId={ns}&days=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var series = (await Body(response)).GetProperty("series");
        series.GetArrayLength().Should().Be(7);
        series.EnumerateArray().Sum(d => d.GetProperty("new").GetInt32()).Should().Be(5);
        series.EnumerateArray().Sum(d => d.GetProperty("resolved").GetInt32()).Should().Be(2);
        series.EnumerateArray().Count(d => d.GetProperty("new").GetInt32() == 0).Should().BeGreaterThanOrEqualTo(4, "empty days are zeros, not gaps");
    }

    [Fact]
    public async Task The_trend_range_is_bounded_and_scoped()
    {
        using var host = Host();
        var ns = await Connect(host.Client, "azure");

        (await host.Client.GetAsync($"/api/v1/dead-letters/trend?namespaceId={ns}&days=31")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/dead-letters/trend?days=7")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync($"/api/v1/dead-letters/trend?namespaceId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var thirty = await Body(await host.Client.GetAsync($"/api/v1/dead-letters/trend?namespaceId={ns}&days=30"));
        thirty.GetProperty("series").GetArrayLength().Should().Be(30);
    }
}
