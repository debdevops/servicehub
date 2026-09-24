using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.Dlq;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// The DLQ monitor (unit 2.1). The tests that matter most are the ones about <i>not</i> concluding:
/// a provider that cannot be read must never look like a queue that drained.
/// </summary>
public sealed class DlqMonitorTests : IAsyncLifetime
{
    private const string AzureCs = "Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=secret-value==";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync(); // proves migration 0002 applies, not just that the model builds
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ServiceHubDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    // ── A fake cloud ─────────────────────────────────────────────────────────────────────────────

    private sealed class FakeCloud(CloudProviderType type, ProviderCapabilities capabilities) : ICloudMessagingProvider, IMessageReceiver
    {
        public List<CloudEntity> Entities { get; } = [];
        public Dictionary<string, List<Message>> DeadLetters { get; } = [];
        public bool ListingFails { get; set; }
        public HashSet<string> IncompleteQueues { get; } = [];
        public HashSet<string> PeekFailsFor { get; } = [];
        public int PeekFailsAfterBatch { get; set; } = -1;
        public int PeekCalls { get; private set; }

        public CloudProviderType ProviderType => type;
        public ProviderCapabilities Capabilities => capabilities;
        public IMessageReceiver GetMessageReceiver() => this;
        public IMessageSender GetMessageSender() => throw new NotSupportedException();
        public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<CloudEntity>>(Entities));

        public Task<Result<EntityScanResult>> ListEntitiesForReconciliationAsync(Guid namespaceId, CancellationToken ct) =>
            Task.FromResult(ListingFails
                ? Result.Failure<EntityScanResult>(Error.ExternalService("Test.Listing", "provider unreachable"))
                : Result.Success(new EntityScanResult { Entities = Entities, IncompleteQueueNames = IncompleteQueues }));

        public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken ct = default)
        {
            PeekCalls++;
            var key = request.SubscriptionName is null ? request.EntityName : $"{request.EntityName}/subscriptions/{request.SubscriptionName}";
            if (PeekFailsFor.Contains(key))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("Test.Peek", "peek failed")));
            }

            var all = DeadLetters.GetValueOrDefault(key) ?? [];
            var from = request.FromSequenceNumber ?? 0;
            var batchIndex = from == 0 ? 0 : (int)(from / request.MaxMessages);
            if (PeekFailsAfterBatch >= 0 && batchIndex > PeekFailsAfterBatch)
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("Test.Peek", "peek failed mid-scan")));
            }

            IReadOnlyList<Message> page = [.. all.Where(m => m.SequenceNumber >= from).Take(request.MaxMessages)];
            return Task.FromResult(Result.Success(page));
        }

        public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<long>> GetMessageCountAsync(Guid namespaceId, string entityName, string? subscriptionName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<bool>> ReplayMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, string? recoveryMarker, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> PurgeMessageAsync(Guid namespaceId, string entityName, string? subscriptionName, long sequenceNumber, bool fromDeadLetter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(Guid namespaceId, string entityName, string? subscriptionName, int maxMessages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static FakeCloud Azure() => new(CloudProviderType.Azure, ProviderCapabilities.Azure);
    private static FakeCloud Aws() => new(CloudProviderType.Aws, ProviderCapabilities.Aws);

    private static CloudEntity Queue(string name, long dead) => new() { Name = name, EntityType = "Queue", DeadLetterCount = dead };

    private static Message Msg(long seq, string id = "") => new()
    {
        MessageId = id == "" ? $"m-{seq}" : id,
        SequenceNumber = seq,
        Body = $"{{\"order\":{seq}}}",
        EnqueuedTime = new DateTimeOffset(2026, 9, 24, 13, 0, 0, TimeSpan.Zero),
        DeadLetterReason = "MaxDeliveryCountExceeded",
        DeliveryCount = 10,
    };

    private static Namespace AzureNs() =>
        Namespace.Create("orders-dev", AzureCs, ownerId: "owner1").Value;

    private static Namespace AwsNs() =>
        Namespace.Create("sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENGbPxRfiCYEXAMPLEKEY",
            provider: CloudProviderType.Aws, awsRegion: "us-east-1", ownerId: "owner1").Value;

    private DlqScanner Scanner(ServiceHubDbContext db, FakeCloud cloud, Dictionary<string, string?>? config = null) =>
        new(db, new CloudProviderRouter([cloud]),
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
            NullLogger<DlqScanner>.Instance, _time);

    private async Task<List<DlqMessage>> Rows()
    {
        await using var db = NewDb();
        return await db.DlqMessages.OrderBy(m => m.SequenceNumber).ToListAsync();
    }

    private async Task<NamespaceScanResult> Scan(FakeCloud cloud, Namespace ns, Dictionary<string, string?>? config = null)
    {
        await using var db = NewDb();
        return await Scanner(db, cloud, config).ScanAsync(ns, CancellationToken.None);
    }

    // ── What gets recorded ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_dead_letter_on_a_namespace_is_recorded_within_one_scan()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 2));
        cloud.DeadLetters["orders"] = [Msg(1), Msg(2)];
        var ns = AzureNs();

        var result = await Scan(cloud, ns);

        result.Outcome.Should().Be(ScanOutcome.Scanned);
        result.NewMessages.Should().Be(2);
        var rows = await Rows();
        rows.Should().HaveCount(2);
        rows[0].Should().Match<DlqMessage>(r =>
            r.NamespaceId == ns.Id && r.OwnerId == "owner1" && r.CloudProvider == CloudProviderType.Azure
            && r.EntityName == "orders" && r.EntityType == ServiceBusEntityType.Queue
            && r.Status == DlqMessageStatus.Active && r.DeadLetterReason == "MaxDeliveryCountExceeded"
            && r.DetectedAtUtc == _time.GetUtcNow() && r.BodyHash.Length == 64);
    }

    [Fact]
    public async Task A_subscription_is_stored_under_one_entity_name_format()
    {
        var cloud = Azure();
        cloud.Entities.Add(new CloudEntity { Name = "events/subscriptions/billing", EntityType = "Subscription", DeadLetterCount = 1 });
        cloud.DeadLetters["events/subscriptions/billing"] = [Msg(1)];

        await Scan(cloud, AzureNs());

        (await Rows()).Single().Should().Match<DlqMessage>(r =>
            r.EntityName == "events/subscriptions/billing" && r.TopicName == "events" && r.EntityType == ServiceBusEntityType.Subscription);
    }

    [Fact]
    public async Task Scanning_twice_does_not_duplicate_and_a_message_that_left_is_resolved_as_vanished()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 2));
        cloud.DeadLetters["orders"] = [Msg(1), Msg(2)];
        var ns = AzureNs();
        await Scan(cloud, ns);

        (await Scan(cloud, ns)).NewMessages.Should().Be(0);
        (await Rows()).Should().HaveCount(2);

        cloud.DeadLetters["orders"] = [Msg(2)];
        cloud.Entities[0] = Queue("orders", 1);
        _time.Advance(TimeSpan.FromMinutes(1));
        var third = await Scan(cloud, ns);

        third.Resolved.Should().Be(1);
        var rows = await Rows();
        rows[0].Should().Match<DlqMessage>(r => r.Status == DlqMessageStatus.Resolved && r.ResolutionCause == DlqResolutionCause.VanishedExternally && r.ResolvedAt == _time.GetUtcNow());
        rows[1].Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task An_emptied_queue_resolves_its_rows_and_a_message_that_comes_back_is_active_again()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 1));
        cloud.DeadLetters["orders"] = [Msg(1)];
        var ns = AzureNs();
        await Scan(cloud, ns);

        cloud.Entities[0] = Queue("orders", 0);
        cloud.DeadLetters["orders"] = [];
        await Scan(cloud, ns);
        (await Rows()).Single().Status.Should().Be(DlqMessageStatus.Resolved);

        cloud.Entities[0] = Queue("orders", 1);
        cloud.DeadLetters["orders"] = [Msg(1)];
        await Scan(cloud, ns);
        var row = (await Rows()).Single();
        row.Status.Should().Be(DlqMessageStatus.Active);
        row.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally, "the earlier record is evidence and is not erased");
    }

    [Theory]
    [InlineData(DlqMessageStatus.Replaying)]
    [InlineData(DlqMessageStatus.Purging)]
    public async Task A_row_that_is_mid_replay_or_mid_purge_is_never_touched(DlqMessageStatus inFlight)
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 1));
        cloud.DeadLetters["orders"] = [Msg(1)];
        var ns = AzureNs();
        await Scan(cloud, ns);
        await using (var db = NewDb())
        {
            (await db.DlqMessages.SingleAsync()).Status = inFlight;
            await db.SaveChangesAsync();
        }

        await Scan(cloud, ns);

        (await Rows()).Single().Status.Should().Be(inFlight);
    }

    // ── The rule that matters: never conclude from what was not seen ─────────────────────────────

    private async Task<(FakeCloud Cloud, Namespace Ns)> SeedTwoActiveRows()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 2));
        cloud.DeadLetters["orders"] = [Msg(1), Msg(2)];
        var ns = AzureNs();
        await Scan(cloud, ns);
        return (cloud, ns);
    }

    [Fact]
    public async Task A_provider_outage_at_listing_deletes_nothing()
    {
        var (cloud, ns) = await SeedTwoActiveRows();
        cloud.ListingFails = true;

        var result = await Scan(cloud, ns);

        result.Outcome.Should().Be(ScanOutcome.Failed);
        (await Rows()).Should().OnlyContain(r => r.Status == DlqMessageStatus.Active);
    }

    [Fact]
    public async Task A_failed_peek_is_not_read_as_an_empty_queue()
    {
        // 4.0.0 resolved these rows: a failed peek reported "0 live", which reconciliation took as "drained".
        var (cloud, ns) = await SeedTwoActiveRows();
        cloud.PeekFailsFor.Add("orders");

        var result = await Scan(cloud, ns);

        result.Unconfirmed.Should().BeGreaterThan(0);
        (await Rows()).Should().OnlyContain(r => r.Status == DlqMessageStatus.Active && r.ResolvedAt == null);
    }

    [Fact]
    public async Task A_peek_that_fails_part_way_resolves_nothing_it_did_not_see()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 150));
        cloud.DeadLetters["orders"] = [.. Enumerable.Range(1, 150).Select(i => Msg(i))];
        var ns = AzureNs();
        await Scan(cloud, ns);
        (await Rows()).Should().HaveCount(150);

        cloud.PeekFailsAfterBatch = 0; // the first 100 come back, the next page fails
        var result = await Scan(cloud, ns);

        result.Unconfirmed.Should().BeGreaterThan(0);
        (await Rows()).Should().OnlyContain(r => r.Status == DlqMessageStatus.Active, "messages 101–150 were not seen, not gone");
    }

    [Fact]
    public async Task An_entity_the_listing_could_not_confirm_is_left_alone_but_a_missing_one_is_resolved()
    {
        var cloud = Azure();
        cloud.Entities.AddRange([Queue("orders", 1), Queue("billing", 1)]);
        cloud.DeadLetters["orders"] = [Msg(1)];
        cloud.DeadLetters["billing"] = [Msg(1)];
        var ns = AzureNs();
        await Scan(cloud, ns);

        // Both queues vanish from the listing, but "billing" is flagged as one the provider could not confirm.
        cloud.Entities.Clear();
        cloud.IncompleteQueues.Add("billing");
        await Scan(cloud, ns);

        var rows = await Rows();
        rows.Single(r => r.EntityName == "orders").Status.Should().Be(DlqMessageStatus.Resolved, "confirmed gone");
        rows.Single(r => r.EntityName == "billing").Status.Should().Be(DlqMessageStatus.Active, "not looked at, not gone");
    }

    // ── AWS and Google: never look on a timer ────────────────────────────────────────────────────

    [Fact]
    public async Task A_cloud_without_repeatable_peek_is_not_scanned_and_is_not_touched()
    {
        var cloud = Aws();
        cloud.Entities.Add(Queue("orders", 3));
        cloud.DeadLetters["orders"] = [Msg(1)];

        var result = await Scan(cloud, AwsNs());

        result.Outcome.Should().Be(ScanOutcome.Skipped);
        result.Reason.Should().Contain("delivery attempt");
        cloud.PeekCalls.Should().Be(0, "peeking there would itself change what is being watched");
        (await Rows()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_operator_can_opt_in_and_a_full_sample_never_resolves_what_lies_beyond_it()
    {
        var cloud = Aws();
        cloud.Entities.Add(Queue("orders", 300));
        cloud.DeadLetters["orders"] = [.. Enumerable.Range(1, 100).Select(i => Msg(i))];
        var ns = AwsNs();
        var optIn = new Dictionary<string, string?> { ["DlqMonitor:AllowDestructivePeek:Aws"] = "true" };
        await Scan(cloud, ns, optIn);
        (await Rows()).Should().HaveCount(100);

        // Same 100 are still first in line, plus an unseen row already stored from an earlier, different sample.
        await using (var db = NewDb())
        {
            db.DlqMessages.Add(new DlqMessage
            {
                MessageId = "older", SequenceNumber = 999, BodyHash = "empty", NamespaceId = ns.Id, CloudProvider = CloudProviderType.Aws,
                OwnerId = "owner1", EntityName = "orders", EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = _time.GetUtcNow(), DetectedAtUtc = _time.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }

        await Scan(cloud, ns, optIn);

        (await Rows()).Single(r => r.MessageId == "older").Status.Should().Be(DlqMessageStatus.Active);
    }

    // ── The agent ────────────────────────────────────────────────────────────────────────────────

    private (DlqMonitorAgent Agent, IServiceProvider Services) BuildAgent(FakeCloud cloud, params Namespace[] namespaces)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ICloudProviderRouter>(new CloudProviderRouter([cloud]));
        services.AddSingleton<TimeProvider>(_time);
        services.AddDbContext<ServiceHubDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<INamespaceRepository, NamespaceRepository>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            foreach (var ns in namespaces)
            {
                repo.AddAsync(ns).GetAwaiter().GetResult().IsSuccess.Should().BeTrue();
            }
        }

        return (new DlqMonitorAgent(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<DlqMonitorAgent>.Instance), provider);
    }

    [Fact]
    public void It_describes_itself_as_a_watcher_that_only_observes()
    {
        var (agent, _) = BuildAgent(Azure());

        agent.Descriptor.Should().Match<AgentDescriptor>(d =>
            d.Id == "dlq-monitor" && d.Kind == AgentKind.Watch && d.Authority == AgentAuthority.Observes && !d.CanAct);
        agent.Descriptor.Purpose.Should().NotBeNullOrWhiteSpace().And.NotContain("Monitor", "the purpose is a hand-written sentence, not the type name");
        agent.Descriptor.Notes.Should().Contain("Google Cloud");
    }

    [Fact]
    public async Task With_nothing_connected_it_is_idle_not_failing()
    {
        var (agent, _) = BuildAgent(Azure());

        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Should().Be(AgentCycleResult.Idle("no clouds are connected"));
    }

    [Fact]
    public async Task A_cycle_records_dead_letters_and_never_reports_a_change()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 2));
        cloud.DeadLetters["orders"] = [Msg(1), Msg(2)];
        var (agent, _) = BuildAgent(cloud, AzureNs());

        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Changed.Should().Be(0, "an Observes agent may not report a mutation; the host would fail it");
        result.Degraded.Should().BeFalse();
        result.Summary.Should().Contain("2 new dead letters");
        (await Rows()).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_cloud_that_cannot_be_read_makes_the_cycle_degraded_and_deletes_nothing()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 1));
        cloud.DeadLetters["orders"] = [Msg(1)];
        var (agent, _) = BuildAgent(cloud, AzureNs());
        await agent.ExecuteCycleAsync(CancellationToken.None);

        cloud.ListingFails = true;
        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Degraded.Should().BeTrue();
        result.Summary.Should().Contain("could not be read");
        (await Rows()).Should().OnlyContain(r => r.Status == DlqMessageStatus.Active);
    }

    [Fact]
    public async Task Rows_whose_cloud_was_removed_are_archived()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 1));
        cloud.DeadLetters["orders"] = [Msg(1)];
        var (agent, services) = BuildAgent(cloud, AzureNs());
        await agent.ExecuteCycleAsync(CancellationToken.None);

        using (var scope = services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            var only = (await repo.GetAllAsync()).Value.Single();
            (await repo.DeleteAsync(only.Id)).IsSuccess.Should().BeTrue();
        }

        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Summary.Should().Contain("archived");
        (await Rows()).Single().Status.Should().Be(DlqMessageStatus.Archived);
    }

    [Fact]
    public async Task The_host_runs_it_like_any_other_agent()
    {
        var cloud = Azure();
        cloud.Entities.Add(Queue("orders", 1));
        cloud.DeadLetters["orders"] = [Msg(1)];
        var (agent, _) = BuildAgent(cloud, AzureNs());
        var registry = new AgentRegistry([agent]);
        var host = new AgentHost([agent], registry, NullLogger<AgentHost>.Instance, _time);

        using var cts = new CancellationTokenSource();
        await host.StartAsync(cts.Token);
        for (var i = 0; i < 400 && registry.StateOf("dlq-monitor")?.LastRunUtc is null; i++)
        {
            await Task.Delay(5);
        }

        await host.StopAsync(CancellationToken.None);

        registry.StateOf("dlq-monitor")!.Health.Should().Be(AgentHealth.Healthy);
        (await Rows()).Should().HaveCount(1);
    }
}
