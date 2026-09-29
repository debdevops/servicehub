using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Unit 2.12: one endpoint's numbers, and the rules that keep them honest — Recovered and Unverified are never
/// merged (V1), zero states are present (V4), and a rate with nothing to divide is null, never 0.
/// </summary>
public sealed class RecoveryQueriesTests : IDisposable
{
    private const string Owner = "owner-1";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RecoveryQueriesTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private async Task Add(RecoveryEntryState state, CloudProviderType provider = CloudProviderType.Azure, Guid? ns = null, string owner = Owner,
        VerificationConfidence? confidence = null, TimeSpan? age = null)
    {
        await using var db = NewDb();
        var begun = DateTimeOffset.UtcNow - (age ?? TimeSpan.FromMinutes(5));
        var op = new RecoveryOperation
        {
            OwnerId = owner, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.Manual, ActorIdentity = "session", ActorKind = RecoveryActorKind.User,
            ScopeDescription = "t", ServiceVersion = "t", OpenedAt = begun, TargetCount = 1,
        };
        db.RecoveryOperations.Add(op);
        db.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
        {
            OperationId = op.Id, OwnerId = owner, NamespaceId = ns ?? Guid.Empty, ProviderSnapshot = provider, BodyHash = "h", TargetEntity = "orders",
            EntityNameSnapshot = "orders-dlq", BegunAt = begun, State = state, VerificationConfidence = confidence,
        });
        await db.SaveChangesAsync();
    }

    private async Task<ServiceHub.Core.Models.RecoverySummary> Summary(string window = "24h", IReadOnlySet<Guid>? allowed = null, Guid? ns = null, CloudProviderType? provider = null)
    {
        await using var db = NewDb();
        return await new RecoveryQueries(db).SummariseAsync(new RecoveryScope(Owner, allowed, ns, provider), window, CancellationToken.None);
    }

    [Fact]
    public async Task No_operations_means_no_rate_not_zero_and_every_state_is_still_listed()
    {
        var s = await Summary();

        s.Total.Should().Be(0);
        s.StayedFixedRate.Should().BeNull();
        s.States.Select(x => x.State).Should().Equal(Enum.GetNames<RecoveryEntryState>());
        s.States.Should().OnlyContain(x => x.Count == 0);
    }

    [Fact]
    public async Task An_unverified_only_window_has_no_rate_neither_0_percent_nor_100()
    {
        await Add(RecoveryEntryState.Unverified);
        await Add(RecoveryEntryState.Unverified, CloudProviderType.Aws);

        var s = await Summary();

        s.StayedFixedRate.Should().BeNull();
        s.States.Single(x => x.State == "Unverified").Count.Should().Be(2);
        s.States.Single(x => x.State == "Recovered").Count.Should().Be(0, "Unverified is never counted as Recovered (V1)");
    }

    [Fact]
    public async Task The_rate_is_recovered_over_recovered_plus_returned_and_unverified_is_on_neither_side()
    {
        for (var i = 0; i < 3; i++) await Add(RecoveryEntryState.Recovered);
        await Add(RecoveryEntryState.Returned, confidence: VerificationConfidence.Exact);
        for (var i = 0; i < 10; i++) await Add(RecoveryEntryState.Unverified);
        await Add(RecoveryEntryState.ExecutionFailed);

        var s = await Summary();

        s.Total.Should().Be(15);
        s.StayedFixedRate.Should().Be(0.75); // 3 / (3 + 1)
        s.ReplaysAccepted.Should().Be(14, "the cloud accepted 3 + 1 + 10; the failed call was never sent");
    }

    [Fact]
    public async Task A_return_carries_how_it_was_matched()
    {
        await Add(RecoveryEntryState.Returned, confidence: VerificationConfidence.Exact);
        await Add(RecoveryEntryState.Returned, confidence: VerificationConfidence.Heuristic);
        await Add(RecoveryEntryState.Returned, confidence: VerificationConfidence.Heuristic);

        var s = await Summary();

        s.ReturnedConfidence.Exact.Should().Be(1);
        s.ReturnedConfidence.Heuristic.Should().Be(2);
    }

    [Fact]
    public async Task The_split_per_provider_is_computed_from_the_same_rows()
    {
        await Add(RecoveryEntryState.Recovered);
        await Add(RecoveryEntryState.Unverified, CloudProviderType.Aws);

        var s = await Summary();

        s.ByProvider.Select(p => (p.Provider, p.Total)).Should().Equal(("azure", 1), ("aws", 1));
        s.ByProvider.Single(p => p.Provider == "azure").StayedFixedRate.Should().Be(1.0);
        s.ByProvider.Single(p => p.Provider == "aws").StayedFixedRate.Should().BeNull();
        s.ByProvider.Sum(p => p.Total).Should().Be(s.Total);
    }

    [Fact]
    public async Task The_allow_list_excludes_a_namespace_and_another_owners_entries_never_count()
    {
        var mine = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        await Add(RecoveryEntryState.Recovered, ns: mine);
        await Add(RecoveryEntryState.Recovered, ns: hidden);
        await Add(RecoveryEntryState.Recovered, ns: mine, owner: "someone-else");

        (await Summary(allowed: new HashSet<Guid> { mine })).Total.Should().Be(1);
        (await Summary()).Total.Should().Be(2);
        (await Summary(ns: hidden)).Total.Should().Be(1);
    }

    [Fact]
    public async Task The_window_bounds_what_counts()
    {
        await Add(RecoveryEntryState.Recovered);
        await Add(RecoveryEntryState.Recovered, age: TimeSpan.FromDays(3));
        await Add(RecoveryEntryState.Recovered, age: TimeSpan.FromDays(20));

        (await Summary("24h")).Total.Should().Be(1);
        (await Summary("7d")).Total.Should().Be(2);
        (await Summary("30d")).Total.Should().Be(3);
        (await Summary("all")).Total.Should().Be(3);
    }

    [Fact]
    public async Task The_list_pages_newest_first_and_can_show_one_state()
    {
        await Add(RecoveryEntryState.Recovered, age: TimeSpan.FromMinutes(30));
        await Add(RecoveryEntryState.Unverified, age: TimeSpan.FromMinutes(10));
        await Add(RecoveryEntryState.Unverified, age: TimeSpan.FromMinutes(20));
        await using var db = NewDb();
        var queries = new RecoveryQueries(db);

        var all = await queries.ListAsync(new RecoveryScope(Owner, null), "24h", null, 1, 2, CancellationToken.None);
        var unverified = await queries.ListAsync(new RecoveryScope(Owner, null), "24h", RecoveryEntryState.Unverified, 1, 25, CancellationToken.None);

        all.Total.Should().Be(3);
        all.Items.Should().HaveCount(2);
        all.Items[0].BegunAt.Should().BeAfter(all.Items[1].BegunAt);
        unverified.Items.Should().OnlyContain(i => i.State == "Unverified").And.HaveCount(2);
    }
}
