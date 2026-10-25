using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Components;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Components;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Components;

/// <summary>Build-plan unit B1: what the component read model returns, with no cloud involved.</summary>
public sealed class ComponentOverviewServiceTests : IAsyncLifetime
{
    private const string Owner = "owner1";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private static Namespace Azure(string name = "orders-dev") =>
        Namespace.Create(name, "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=a;SharedAccessKey=b=", ownerId: Owner).Value;

    private static Namespace Aws() =>
        Namespace.Create("sqs.eu-west-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENGbPxRfiCYEXAMPLEKEY",
            provider: CloudProviderType.Aws, awsRegion: "eu-west-1", ownerId: Owner).Value;

    private static Namespace Gcp() =>
        Namespace.Create("my-project", "{}", provider: CloudProviderType.Gcp, gcpProjectId: "my-project", ownerId: Owner).Value;

    private static CloudEntity Entity(string name, string type, CloudProviderType provider, long dead = 0, string? target = null) =>
        new() { Name = name, EntityType = type, Provider = provider, DeadLetterCount = dead, ActiveMessageCount = 7, DeadLetterTargetName = target };

    private sealed class Cloud(ProviderCapabilities caps, Func<Result<IReadOnlyList<CloudEntity>>> list)
    {
        public ProviderCapabilities Caps { get; } = caps;
        public Result<IReadOnlyList<CloudEntity>> List() => list();
    }

    private static Cloud Lists(ProviderCapabilities caps, params CloudEntity[] entities) =>
        new(caps, () => Result.Success<IReadOnlyList<CloudEntity>>(entities));

    private static ICloudProviderRouter Router(Dictionary<CloudProviderType, Cloud> clouds)
    {
        var router = new Mock<ICloudProviderRouter>();
        router.Setup(r => r.IsRegistered(It.IsAny<CloudProviderType>())).Returns<CloudProviderType>(clouds.ContainsKey);
        router.Setup(r => r.Resolve(It.IsAny<CloudProviderType>())).Returns<CloudProviderType>(type =>
        {
            var provider = new Mock<ICloudMessagingProvider>();
            provider.SetupGet(p => p.Capabilities).Returns(clouds[type].Caps);
            provider.Setup(p => p.ListEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => clouds[type].List());
            return provider.Object;
        });
        return router.Object;
    }

    private static INamespaceRepository Repo(params Namespace[] all)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByOwnerAsync(Owner, It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlySet<Guid>? allowed, CancellationToken _) =>
                Result.Success<IReadOnlyList<Namespace>>([.. all.Where(n => allowed is null || allowed.Contains(n.Id))]));
        return repo.Object;
    }

    private static IPendingWorkService Pending(params PendingWorkItem[] items)
    {
        var pending = new Mock<IPendingWorkService>();
        pending.Setup(p => p.ListAsync(It.IsAny<PendingWorkScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingWorkPage(items, items.Length, [], 0));
        return pending.Object;
    }

    private static PendingWorkItem Item(string kind, Namespace ns, string entity) =>
        new(kind, Guid.NewGuid().ToString(), Guid.NewGuid(), null, 1, ns.Id, ns.Name, null, "dev", entity, null, null, null, "CODE", "Sentence", DateTimeOffset.UtcNow);

    private async Task<ComponentOverviewService> ServiceAsync(Dictionary<CloudProviderType, Cloud> clouds, IPendingWorkService? pending = null, params Namespace[] namespaces)
    {
        await using (var db = NewDb())
        {
            db.Namespaces.AddRange(namespaces);
            await db.SaveChangesAsync();
        }

        return new ComponentOverviewService(NewDb(), Repo(namespaces), Router(clouds), pending ?? Pending());
    }

    [Fact]
    public async Task One_AWS_connection_is_two_components_and_the_dead_letter_queue_is_not_a_row_of_its_own()
    {
        var ns = Aws();
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Aws] = Lists(ProviderCapabilities.Aws,
                Entity("orders", "Queue", CloudProviderType.Aws, dead: 4, target: "orders-dlq"),
                Entity("orders-dlq", "Queue", CloudProviderType.Aws, dead: 0),
                Entity("fanout/subscriptions/billing", "Subscription", CloudProviderType.Aws, dead: 2),
                Entity("fanout", "Topic", CloudProviderType.Aws)),
        }, null, ns);

        var overview = await service.GetAsync(Owner, null, null, default);

        overview.Components.Select(c => c.ComponentKey).Should().BeEquivalentTo("sqs", "sns");
        var sqs = overview.Components.Single(c => c.ComponentKey == "sqs");
        sqs.Entities.Select(e => e.Name).Should().Equal("orders");
        sqs.DeadLetters.Should().Be(new Count(4, CountKind.Approximate));
        var sns = overview.Components.Single(c => c.ComponentKey == "sns");
        sns.Entities.Should().ContainSingle().Which.Should().Match<ComponentEntity>(e => e.Name == "billing" && e.Parent == "fanout");
    }

    [Fact]
    public async Task Each_cloud_reports_its_counts_in_the_way_its_adapter_says()
    {
        var azure = Azure();
        var gcp = Gcp();
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Azure] = Lists(ProviderCapabilities.Azure, Entity("orders", "Queue", CloudProviderType.Azure, dead: 3)),
            [CloudProviderType.Gcp] = Lists(ProviderCapabilities.Gcp, Entity("t/subscriptions/s", "Subscription", CloudProviderType.Gcp, dead: 99)),
        }, null, azure, gcp);

        var rows = (await service.GetAsync(Owner, null, null, default)).Components;

        rows.Single(c => c.Cloud == CloudKind.Azure).DeadLetters.Should().Be(new Count(3, CountKind.Exact));
        var google = rows.Single(c => c.Cloud == CloudKind.Gcp);
        google.DeadLetters.Should().Be(new Count(null, CountKind.NotReported), "Pub/Sub has no count, so the adapter's 0 or 99 is never shown");
        google.Entities.Single().DeadLetters.Value.Should().BeNull();
    }

    [Fact]
    public async Task An_unreadable_namespace_is_a_Cant_reach_row_with_no_numbers_and_a_sanitised_error()
    {
        var ns = Azure();
        var clouds = new Dictionary<CloudProviderType, Cloud>
        {
            [CloudProviderType.Azure] = new(ProviderCapabilities.Azure, () =>
                Result.Failure<IReadOnlyList<CloudEntity>>(Error.ExternalService("x", "denied SharedAccessKey=abcd1234secret=="))),
        };
        var service = await ServiceAsync(clouds, null, ns);

        var row = (await service.GetAsync(Owner, null, null, default)).Components.Should().ContainSingle().Subject;

        row.Reachable.Should().BeFalse();
        row.DeadLetters.Should().Be(new Count(null, CountKind.NotRead));
        row.LastError.Should().NotBeNull().And.NotContain("abcd1234secret");
        row.ObservedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_connection_with_no_store_credential_has_no_message_store_row_instead_of_a_false_Cant_reach()
    {
        // An Azure sign-in connection exists to read Logic Apps and Event Grid; it holds no connection string, so there are no queues to count.
        var ns = Azure("azure-entra");
        var clouds = new Dictionary<CloudProviderType, Cloud>
        {
            [CloudProviderType.Azure] = new(ProviderCapabilities.Azure, () =>
                Result.Failure<IReadOnlyList<CloudEntity>>(Error.Validation(ErrorCodes.Namespace.ConnectionStringRequired, "Namespace does not have a connection string configured."))),
        };
        var service = await ServiceAsync(clouds, null, ns);

        (await service.GetAsync(Owner, null, null, default)).Components.Should().BeEmpty();
    }

    [Fact]
    public async Task A_readable_connection_with_no_queue_is_still_its_components_reachable_and_empty_not_missing()
    {
        var ns = Aws();
        var service = await ServiceAsync(new() { [CloudProviderType.Aws] = Lists(ProviderCapabilities.Aws) }, null, ns);

        var rows = (await service.GetAsync(Owner, null, null, default)).Components;

        rows.Select(c => c.ComponentKey).Should().BeEquivalentTo("sqs", "sns");
        rows.Should().OnlyContain(c => c.Reachable && c.Entities.Count == 0 && c.NeedsYou == 0);
        rows.Should().OnlyContain(c => c.DeadLetters.Value == 0 && c.DeadLetters.Kind == CountKind.Approximate);
    }

    [Fact]
    public async Task One_unreadable_namespace_does_not_hide_the_others()
    {
        var bad = Azure("bad-ns");
        var good = Aws();
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Azure] = new(ProviderCapabilities.Azure, () => throw new InvalidOperationException("boom")),
            [CloudProviderType.Aws] = Lists(ProviderCapabilities.Aws, Entity("orders", "Queue", CloudProviderType.Aws, dead: 1)),
        }, null, bad, good);

        var rows = (await service.GetAsync(Owner, null, null, default)).Components;

        rows.Single(c => c.NamespaceId == bad.Id).Reachable.Should().BeFalse();
        rows.Single(c => c.NamespaceId == good.Id).Reachable.Should().BeTrue();
    }

    [Fact]
    public async Task An_entity_the_catalogue_does_not_know_is_kept_in_an_Other_component_of_unknown_class()
    {
        var gcp = Gcp();
        var service = await ServiceAsync(new()
        {
            // The catalogue maps only a Pub/Sub topic and subscription; a "queue" kind has no component.
            [CloudProviderType.Gcp] = Lists(ProviderCapabilities.Gcp, Entity("odd", "Queue", CloudProviderType.Gcp)),
        }, null, gcp);

        var row = (await service.GetAsync(Owner, null, null, default)).Components.Should().ContainSingle().Subject;

        row.ComponentKey.Should().Be(ComponentOverviewService.UnknownComponentKey);
        row.Class.Should().Be(RecoverabilityClass.R0Unknown);
        row.Entities.Should().ContainSingle(e => e.Name == "odd");
    }

    [Fact]
    public async Task Needs_you_splits_approvals_from_hand_offs_and_ignores_rules_and_agents()
    {
        var ns = Azure();
        var pending = Pending(
            Item("approval", ns, "orders"), Item("approval", ns, "orders"), Item("unresolved", ns, "orders"),
            Item("rule", ns, "orders"), Item("agent", ns, "orders"), Item("approval", ns, "other"));
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Azure] = Lists(ProviderCapabilities.Azure, Entity("orders", "Queue", CloudProviderType.Azure), Entity("other", "Queue", CloudProviderType.Azure)),
        }, pending, ns);

        var row = (await service.GetAsync(Owner, null, null, default)).Components.Single();

        (row.Approvals, row.HandOffs, row.NeedsYou).Should().Be((3, 1, 4));
        row.Entities.Single(e => e.Name == "orders").NeedsYou.Should().Be(3);
        row.Entities.Single(e => e.Name == "other").NeedsYou.Should().Be(1);
    }

    [Fact]
    public async Task Totals_equal_the_sum_of_the_entities()
    {
        var ns = Azure();
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Azure] = Lists(ProviderCapabilities.Azure,
                Entity("a", "Queue", CloudProviderType.Azure, dead: 5), Entity("b", "Queue", CloudProviderType.Azure, dead: 8),
                Entity("t/subscriptions/s", "Subscription", CloudProviderType.Azure, dead: 2)),
        }, Pending(Item("approval", ns, "a"), Item("unresolved", ns, "b")), ns);

        var row = (await service.GetAsync(Owner, null, null, default)).Components.Single();

        row.DeadLetters.Value.Should().Be(row.Entities.Sum(e => e.DeadLetters.Value));
        row.NeedsYou.Should().Be(row.Entities.Sum(e => e.NeedsYou));
    }

    [Fact]
    public async Task Recorded_is_shown_only_where_ServiceHub_does_not_watch_and_says_when_it_never_looked()
    {
        var azure = Azure();
        var aws = Aws();
        await using (var db = NewDb())
        {
            db.Namespaces.AddRange(azure, aws);
            await db.SaveChangesAsync();
            db.DlqMessages.AddRange(Message(aws, "orders", DlqMessageStatus.Active), Message(aws, "orders", DlqMessageStatus.Active), Message(aws, "orders", DlqMessageStatus.Replayed));
            await db.SaveChangesAsync();
        }

        var service = new ComponentOverviewService(NewDb(), Repo(azure, aws), Router(new()
        {
            [CloudProviderType.Azure] = Lists(ProviderCapabilities.Azure, Entity("orders", "Queue", CloudProviderType.Azure)),
            [CloudProviderType.Aws] = Lists(ProviderCapabilities.Aws, Entity("orders", "Queue", CloudProviderType.Aws), Entity("quiet", "Queue", CloudProviderType.Aws)),
        }), Pending());

        var rows = (await service.GetAsync(Owner, null, null, default)).Components;

        rows.Single(c => c.Cloud == CloudKind.Azure).Recorded.Should().BeNull();
        var sqs = rows.Single(c => c.Cloud == CloudKind.Aws);
        sqs.Recorded!.Messages.Should().Be(2);
        sqs.Entities.Single(e => e.Name == "orders").Recorded!.Messages.Should().Be(2);
        sqs.Entities.Single(e => e.Name == "quiet").Recorded!.Messages.Should().BeNull("nothing was ever recorded for it");
    }

    [Fact]
    public async Task The_cloud_filter_and_the_allow_list_narrow_the_result_and_a_hidden_component_is_null()
    {
        var azure = Azure();
        var aws = Aws();
        var service = await ServiceAsync(new()
        {
            [CloudProviderType.Azure] = Lists(ProviderCapabilities.Azure, Entity("orders", "Queue", CloudProviderType.Azure)),
            [CloudProviderType.Aws] = Lists(ProviderCapabilities.Aws, Entity("orders", "Queue", CloudProviderType.Aws)),
        }, null, azure, aws);

        (await service.GetAsync(Owner, null, CloudKind.Aws, default)).Components.Should().ContainSingle(c => c.Cloud == CloudKind.Aws);
        (await service.GetAsync(Owner, new HashSet<Guid> { azure.Id }, null, default)).Components.Should().ContainSingle(c => c.NamespaceId == azure.Id);

        (await service.GetOneAsync(Owner, new HashSet<Guid> { azure.Id }, aws.Id, "sqs", default)).Should().BeNull();
        (await service.GetOneAsync(Owner, null, aws.Id, "nope", default)).Should().BeNull();
        (await service.GetOneAsync(Owner, null, aws.Id, "sqs", default)).Should().NotBeNull();
    }

    private static DlqMessage Message(Namespace ns, string entity, DlqMessageStatus status) => new()
    {
        MessageId = Guid.NewGuid().ToString(), SequenceNumber = Random.Shared.NextInt64(), BodyHash = "empty", NamespaceId = ns.Id,
        CloudProvider = ns.Provider, OwnerId = Owner, EntityName = entity, EntityType = ServiceBusEntityType.Queue,
        EnqueuedTimeUtc = DateTimeOffset.UtcNow, DetectedAtUtc = DateTimeOffset.UtcNow, Status = status,
    };
}
