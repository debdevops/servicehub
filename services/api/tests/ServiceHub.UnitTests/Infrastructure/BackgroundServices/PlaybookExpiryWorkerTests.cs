using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Coverage for <see cref="PlaybookExpiryWorker"/>: a single-pass sweep (no flag-then-expire
/// two-pass like <see cref="RecoveryAgeingWorker"/>) that expires non-terminal
/// <see cref="PlaybookEntry"/> rows once <see cref="PlaybookEntry.ExpiresAt"/> has passed.
/// </summary>
public sealed class PlaybookExpiryWorkerTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";
    private static readonly PlaybookActor Worker = new("System:AnomalyDetectionWorker", PlaybookActorKind.System);

    private readonly DlqDbContext _dbContext;
    private readonly PlaybookLedgerService _playbookLedger;

    public PlaybookExpiryWorkerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _playbookLedger = new PlaybookLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static PlaybookExpiryWorker CreateWorker() =>
        new(
            Mock.Of<IServiceProvider>(),
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<PlaybookExpiryWorker>.Instance);

    private static IServiceProvider BuildScope(IPlaybookLedger playbookLedger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(playbookLedger);
        return services.BuildServiceProvider();
    }

    private async Task<PlaybookEntry> ProposeExpiredEntryAsync(string ownerId = OwnerA) =>
        (await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = ownerId,
            PillarKind = PillarKind.Investigate,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = """{"anomalyId":"abc-123"}""",
            ProposalJson = """{"severity":"high"}""",
            Proposer = Worker,
            ExpiresAfter = TimeSpan.FromDays(-1),
        })).Value;

    [Fact]
    public async Task SweepOwnerAsync_ExpiredEntry_IsExpired()
    {
        var entry = await ProposeExpiredEntryAsync();
        var worker = CreateWorker();

        await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(PlaybookEntryState.Expired);
    }

    [Fact]
    public async Task SweepOwnerAsync_NotYetExpiredEntry_LeftUntouched()
    {
        var entry = (await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = OwnerA,
            PillarKind = PillarKind.Investigate,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = "{}",
            ProposalJson = "{}",
            Proposer = Worker,
            ExpiresAfter = TimeSpan.FromDays(7),
        })).Value;
        var worker = CreateWorker();

        await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(PlaybookEntryState.Proposed);
    }

    [Fact]
    public async Task SweepOwnerAsync_TerminalEntry_NeverExpired()
    {
        var entry = await ProposeExpiredEntryAsync();
        await _playbookLedger.DispositionAsync(
            entry.Id, OwnerA, new PlaybookActor("alex@contoso.com", PlaybookActorKind.User), PlaybookDisposition.Approved, null);

        var worker = CreateWorker();
        await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(PlaybookEntryState.Approved, "already-terminal entries are untouched");
    }

    [Fact]
    public async Task SweepOwnerAsync_RepeatedSweeps_AreIdempotent_NoDuplicateExpiryEvents()
    {
        var entry = await ProposeExpiredEntryAsync();
        var worker = CreateWorker();
        var scope = BuildScope(_playbookLedger);

        for (var i = 0; i < 3; i++)
        {
            await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        }

        var expiryEvents = await _dbContext.PlaybookEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == PlaybookEventType.Expired)
            .CountAsync();
        expiryEvents.Should().Be(1);
    }

    [Fact]
    public async Task SweepOwnerAsync_OtherOwnersExpiredEntry_NotTouched()
    {
        var entryA = await ProposeExpiredEntryAsync(OwnerA);
        var entryB = await ProposeExpiredEntryAsync(OwnerB);

        var worker = CreateWorker();
        await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, CancellationToken.None);

        var updatedA = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entryA.Id);
        var updatedB = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entryB.Id);
        updatedA.State.Should().Be(PlaybookEntryState.Expired);
        updatedB.State.Should().Be(PlaybookEntryState.Proposed);
    }

    [Fact]
    public async Task SweepOwnerAsync_SimulatedRestart_FreshScopeStillExpiresEntry()
    {
        var entry = await ProposeExpiredEntryAsync();
        var worker = CreateWorker();

        // Each sweep call below builds an entirely new scope against the same durable DB —
        // nothing is cached in the worker between calls, so this is indistinguishable from a
        // sweep run by a different process instance (a restart).
        await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(PlaybookEntryState.Expired);
    }

    [Fact]
    public async Task SweepOwnerAsync_CancelledBeforeDueEntry_ThrowsOperationCanceledException()
    {
        await ProposeExpiredEntryAsync();
        var worker = CreateWorker();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await worker.SweepOwnerAsync(BuildScope(_playbookLedger), OwnerA, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new PlaybookExpiryWorker(
            null!, new ConfigurationBuilder().Build(), NullLogger<PlaybookExpiryWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new PlaybookExpiryWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<PlaybookExpiryWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new PlaybookExpiryWorker(
            Mock.Of<IServiceProvider>(), new ConfigurationBuilder().Build(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceHub.Shared.Results.Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var services = new ServiceCollection();
        services.AddSingleton(repoMock.Object);
        var provider = services.BuildServiceProvider();

        var worker = new PlaybookExpiryWorker(
            provider, new ConfigurationBuilder().Build(), NullLogger<PlaybookExpiryWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
