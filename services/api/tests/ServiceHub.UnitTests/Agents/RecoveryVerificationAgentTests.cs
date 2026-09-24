using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// Unit 2.8, and rule R4: the same replay ends differently on a cloud that can prove the queue stayed empty and
/// one that cannot — decided by the namespace's CAPABILITY, never by the provider's name — and "did not come
/// back" is never claimed without that proof.
/// </summary>
public sealed class RecoveryVerificationAgentTests : IDisposable
{
    private const string Owner = "owner1";
    private static readonly RecoveryActor Actor = new("session", RecoveryActorKind.User);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RecoveryVerificationAgentTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private sealed class CapabilityCloud(CloudProviderType type, ProviderCapabilities capabilities) : ICloudMessagingProvider
    {
        public CloudProviderType ProviderType => type;
        public ProviderCapabilities Capabilities => capabilities;
        public IMessageReceiver GetMessageReceiver() => throw new NotSupportedException();
        public IMessageSender GetMessageSender() => throw new NotSupportedException();
        public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) => throw new NotSupportedException();
    }

    private static Namespace AzureNs() =>
        Namespace.Create("orders-dev", "Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=secret-value==", ownerId: Owner).Value;

    private async Task<Guid> Observing(Namespace? ns, TimeSpan windowEndsIn)
    {
        await using var db = NewDb();
        var ledger = new RecoveryLedgerService(db);
        var op = (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = Owner, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.Manual, Actor = Actor, ScopeDescription = "t", TargetCount = 1,
        })).Value;
        var entry = (await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = op.Id, OwnerId = Owner, Actor = Actor, NamespaceId = ns?.Id, BodyHash = "h", TargetEntity = "orders",
        })).Value;
        await ledger.RecordExecutionAsync(new RecordExecutionRequest { EntryId = entry.Id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryExecutionOutcome.Accepted });

        var tracked = await db.RecoveryLedgerEntries.SingleAsync(e => e.Id == entry.Id);
        tracked.ObservationWindowEndsAt = DateTimeOffset.UtcNow + windowEndsIn;
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private RecoveryVerificationAgent Agent(Namespace? ns, ProviderCapabilities? capabilities, CloudProviderType type = CloudProviderType.Azure)
    {
        var repo = new Mock<INamespaceRepository>();
        IReadOnlyList<Namespace> all = ns is null ? [] : [ns];
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(all));
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => ns is not null && ns.Id == id
                ? Result<Namespace>.Success(ns)
                : Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "gone")));

        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        services.AddScoped<IRecoveryLedger>(sp => new RecoveryLedgerService(sp.GetRequiredService<ServiceHubDbContext>()));
        services.AddSingleton(repo.Object);
        services.AddSingleton<ICloudProviderRouter>(new CloudProviderRouter(capabilities is null ? [] : [new CapabilityCloud(type, capabilities)]));
        return new RecoveryVerificationAgent(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), NullLogger<RecoveryVerificationAgent>.Instance);
    }

    private async Task<RecoveryLedgerEntry> Entry(Guid id)
    {
        await using var db = NewDb();
        return await db.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public void It_watches_and_never_acts()
    {
        var d = Agent(null, null).Descriptor;
        d.Kind.Should().Be(AgentKind.Watch);
        d.Authority.Should().Be(AgentAuthority.Observes);
        d.CanAct.Should().BeFalse();
    }

    [Fact]
    public async Task A_window_that_ended_on_a_cloud_that_can_prove_absence_is_verified()
    {
        var ns = AzureNs();
        var id = await Observing(ns, TimeSpan.FromMinutes(-1));

        var result = await Agent(ns, ProviderCapabilities.Azure).ExecuteCycleAsync(CancellationToken.None);

        (await Entry(id)).State.Should().Be(RecoveryEntryState.Recovered);
        result.Changed.Should().Be(0, "an Observes agent's bookkeeping is not a change to the outside world");
    }

    [Fact]
    public async Task The_same_replay_on_a_cloud_that_cannot_prove_absence_is_unverified_never_recovered()
    {
        var ns = AzureNs();
        var id = await Observing(ns, TimeSpan.FromMinutes(-1));

        await Agent(ns, ProviderCapabilities.Aws, CloudProviderType.Azure).ExecuteCycleAsync(CancellationToken.None);

        var entry = await Entry(id);
        entry.State.Should().Be(RecoveryEntryState.Unverified);
        entry.Disposition.Should().Be(RecoveryDisposition.Unverified);
    }

    [Fact]
    public async Task The_decision_follows_the_capability_and_not_the_provider_name()
    {
        // A namespace whose provider is named Azure but whose capabilities cannot prove absence is Unverified;
        // one named AWS with a capability that can is Verified. The name decides nothing.
        var azureNs = AzureNs();
        var a = await Observing(azureNs, TimeSpan.FromMinutes(-1));
        await Agent(azureNs, ProviderCapabilities.Azure with { CanProveDlqAbsence = false }).ExecuteCycleAsync(CancellationToken.None);
        (await Entry(a)).State.Should().Be(RecoveryEntryState.Unverified);

        var awsNs = Namespace.Create("sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENGbPxRfiCYEXAMPLEKEY",
            provider: CloudProviderType.Aws, awsRegion: "us-east-1", ownerId: Owner).Value;
        var b = await Observing(awsNs, TimeSpan.FromMinutes(-1));
        await Agent(awsNs, ProviderCapabilities.Aws with { CanProveDlqAbsence = true }, CloudProviderType.Aws).ExecuteCycleAsync(CancellationToken.None);
        (await Entry(b)).State.Should().Be(RecoveryEntryState.Recovered);
    }

    [Fact]
    public async Task A_namespace_that_is_gone_means_nobody_was_watching()
    {
        var ns = AzureNs();
        var id = await Observing(ns, TimeSpan.FromMinutes(-1));

        // The namespace list still names it (so the owner is swept), but it can no longer be loaded.
        var agent = Agent(ns, ProviderCapabilities.Azure);
        var (outcome, reason) = await RecoveryVerificationAgent.DetermineCoverageAsync(
            await Entry(id), Mock.Of<INamespaceRepository>(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) ==
                Task.FromResult(Result<Namespace>.Failure(Error.NotFound("x", "gone")))),
            new CloudProviderRouter([]), CancellationToken.None);

        outcome.Should().Be(RecoveryObservationOutcome.ObservationUnavailable);
        reason.Should().Be("NAMESPACE_DEREGISTERED");
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task A_window_still_open_is_left_alone()
    {
        var ns = AzureNs();
        var id = await Observing(ns, TimeSpan.FromHours(3));

        await Agent(ns, ProviderCapabilities.Azure).ExecuteCycleAsync(CancellationToken.None);

        (await Entry(id)).State.Should().Be(RecoveryEntryState.Observing);
    }

    [Fact]
    public async Task A_return_recorded_first_is_not_overwritten_by_the_window_closing()
    {
        var ns = AzureNs();
        var id = await Observing(ns, TimeSpan.FromMinutes(-1));
        await using (var db = NewDb())
        {
            await new RecoveryLedgerService(db).RecordObservationAsync(new RecordObservationRequest
            {
                EntryId = id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryObservationOutcome.RecurrenceObserved, Confidence = VerificationConfidence.Exact,
            });
        }

        await Agent(ns, ProviderCapabilities.Azure).ExecuteCycleAsync(CancellationToken.None);

        (await Entry(id)).State.Should().Be(RecoveryEntryState.Returned);
    }
}
