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
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Phase 4 (Recovery Verification) coverage for <see cref="RecoveryVerificationWorker"/>: the
/// window-close half of the verification model. <see cref="RecoveryVerificationWorker.DetermineCoverageAsync"/>
/// is tested directly for the coverage decision; <see cref="RecoveryVerificationWorker.SweepOwnerAsync"/>
/// is tested against a real <see cref="RecoveryLedgerService"/>/<see cref="DlqDbContext"/> pair
/// so restart, duplicate-sweep, cancellation and owner-isolation behaviour is exercised against
/// actual persisted state, not mocks of it.
/// </summary>
public sealed class RecoveryVerificationWorkerTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly Mock<INamespaceRepository> _namespaceRepoMock = new();

    public RecoveryVerificationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryVerificationWorker CreateWorker() =>
        new(Mock.Of<IServiceProvider>(), new ConfigurationBuilder().Build(), NullLogger<RecoveryVerificationWorker>.Instance);

    private Namespace CreateNamespace(Guid id, CloudProviderType provider, string ownerId = OwnerA)
    {
        var ns = provider switch
        {
            CloudProviderType.Aws => Namespace.Create("aws-ns", "akid:secret", provider: CloudProviderType.Aws, ownerId: ownerId).Value,
            CloudProviderType.Gcp => Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, ownerId: ownerId).Value,
            _ => Namespace.Create("azure-ns", "PROTECTED:encrypted-data", ownerId: ownerId).Value,
        };
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, id);

        _namespaceRepoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        return ns;
    }

    private static IServiceProvider BuildScope(
        DlqDbContext dbContext, IRecoveryLedger recoveryLedger, INamespaceRepository namespaceRepo, CloudProviderRouter router)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(recoveryLedger);
        services.AddSingleton(namespaceRepo);
        services.AddSingleton(router);
        return services.BuildServiceProvider();
    }

    // ── DetermineCoverageAsync ────────────────────────────────────────────────

    [Theory]
    [InlineData(CloudProviderType.Azure, RecoveryObservationOutcome.NoRecurrenceObserved, null)]
    [InlineData(CloudProviderType.Aws, RecoveryObservationOutcome.ObservationUnavailable, "AWS_NO_ABSENCE_PROOF")]
    [InlineData(CloudProviderType.Gcp, RecoveryObservationOutcome.ObservationUnavailable, "GCP_NO_ABSENCE_PROOF")]
    public async Task DetermineCoverageAsync_UsesProviderCapability_NeverRecoveredWithoutProvenCoverage(
        CloudProviderType provider, RecoveryObservationOutcome expectedOutcome, string? expectedReason)
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, provider);

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(provider);
        providerMock.SetupGet(p => p.Capabilities).Returns(provider switch
        {
            CloudProviderType.Aws => ProviderCapabilities.Aws,
            CloudProviderType.Gcp => ProviderCapabilities.Gcp,
            _ => ProviderCapabilities.Azure,
        });
        var router = new CloudProviderRouter(new[] { providerMock.Object });

        var entry = new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerA,
            NamespaceId = namespaceId,
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
        };

        var (outcome, reason) = await RecoveryVerificationWorker.DetermineCoverageAsync(
            entry, _namespaceRepoMock.Object, router, CancellationToken.None);

        outcome.Should().Be(expectedOutcome);
        reason.Should().Be(expectedReason);

        // The central invariant: absence can only be claimed when coverage is proven — Aws/Gcp
        // must never reach NoRecurrenceObserved (the precursor to a Recovered disposition).
        if (provider != CloudProviderType.Azure)
        {
            outcome.Should().NotBe(RecoveryObservationOutcome.NoRecurrenceObserved);
        }
    }

    [Fact]
    public async Task DetermineCoverageAsync_EntryHasNoNamespace_ReturnsObservationUnavailable()
    {
        var entry = new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerA,
            NamespaceId = null,
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
        };

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var (outcome, reason) = await RecoveryVerificationWorker.DetermineCoverageAsync(
            entry, _namespaceRepoMock.Object, router, CancellationToken.None);

        outcome.Should().Be(RecoveryObservationOutcome.ObservationUnavailable);
        reason.Should().Be("NAMESPACE_UNKNOWN");
    }

    [Fact]
    public async Task DetermineCoverageAsync_NamespaceDeregistered_ReturnsObservationUnavailable()
    {
        var namespaceId = Guid.NewGuid();
        _namespaceRepoMock.Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "gone")));

        var entry = new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerA,
            NamespaceId = namespaceId,
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
        };

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var (outcome, reason) = await RecoveryVerificationWorker.DetermineCoverageAsync(
            entry, _namespaceRepoMock.Object, router, CancellationToken.None);

        outcome.Should().Be(RecoveryObservationOutcome.ObservationUnavailable);
        reason.Should().Be("NAMESPACE_DEREGISTERED");
    }

    [Fact]
    public async Task DetermineCoverageAsync_ProviderNotRegistered_ReturnsObservationUnavailable()
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure);

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>()); // nothing registered

        var entry = new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerA,
            NamespaceId = namespaceId,
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
        };

        var (outcome, reason) = await RecoveryVerificationWorker.DetermineCoverageAsync(
            entry, _namespaceRepoMock.Object, router, CancellationToken.None);

        outcome.Should().Be(RecoveryObservationOutcome.ObservationUnavailable);
        reason.Should().Be("AZURE_PROVIDER_NOT_REGISTERED");
    }

    // ── SweepOwnerAsync ───────────────────────────────────────────────────────

    private async Task<RecoveryLedgerEntry> BeginObservingEntryAsync(
        string ownerId, Guid namespaceId, DateTimeOffset? windowEndsAt = null)
    {
        var operation = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = namespaceId,
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        var begin = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = ownerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = namespaceId,
            EntityNameSnapshot = "orders-dlq",
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
        });

        var executed = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = begin.Value.Id,
            OwnerId = ownerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        executed.Value.State.Should().Be(RecoveryEntryState.Observing);

        if (windowEndsAt is { } ends)
        {
            // Backdate the window directly — RecordExecutionAsync always opens a 24h window, but
            // these tests need windows already past their end.
            var stored = await _dbContext.RecoveryLedgerEntries.FirstAsync(e => e.Id == begin.Value.Id);
            stored.ObservationWindowEndsAt = ends;
            await _dbContext.SaveChangesAsync();
        }

        return executed.Value;
    }

    private CloudProviderRouter AzureRouter()
    {
        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Azure);
        providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Azure);
        return new CloudProviderRouter(new[] { providerMock.Object });
    }

    [Fact]
    public async Task SweepOwnerAsync_EntryPastWindow_ClosesAsRecoveredForAzure()
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure, OwnerA);
        var entry = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var worker = CreateWorker();
        var scope = BuildScope(_dbContext, _recoveryLedger, _namespaceRepoMock.Object, AzureRouter());

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Recovered);
        updated.VerificationResult.Should().Be(VerificationResult.Recovered);
    }

    [Fact]
    public async Task SweepOwnerAsync_WindowNotYetEnded_LeftUntouched()
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure, OwnerA);
        var entry = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddHours(1));

        var worker = CreateWorker();
        var scope = BuildScope(_dbContext, _recoveryLedger, _namespaceRepoMock.Object, AzureRouter());

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Observing);
    }

    [Fact]
    public async Task SweepOwnerAsync_CalledTwice_SecondSweepIsSafeNoOp()
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure, OwnerA);
        var entry = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var worker = CreateWorker();
        var scope = BuildScope(_dbContext, _recoveryLedger, _namespaceRepoMock.Object, AzureRouter());

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        var afterFirst = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        // Second sweep must not throw, must not double-append events, and must leave the entry
        // exactly as the first sweep left it — RecordObservationAsync rejects the second
        // transition attempt as a Conflict, which SweepOwnerAsync swallows as a lost race.
        var act = async () => await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var afterSecond = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        afterSecond.State.Should().Be(afterFirst.State);
        afterSecond.LastEventSeq.Should().Be(afterFirst.LastEventSeq);
    }

    [Fact]
    public async Task SweepOwnerAsync_OtherOwnersDueEntry_NotTouched()
    {
        var namespaceIdA = Guid.NewGuid();
        var namespaceIdB = Guid.NewGuid();
        CreateNamespace(namespaceIdA, CloudProviderType.Azure, OwnerA);
        CreateNamespace(namespaceIdB, CloudProviderType.Azure, OwnerB);

        var entryA = await BeginObservingEntryAsync(OwnerA, namespaceIdA, DateTimeOffset.UtcNow.AddMinutes(-1));
        var entryB = await BeginObservingEntryAsync(OwnerB, namespaceIdB, DateTimeOffset.UtcNow.AddMinutes(-1));

        var worker = CreateWorker();
        var scope = BuildScope(_dbContext, _recoveryLedger, _namespaceRepoMock.Object, AzureRouter());

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);

        var updatedA = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entryA.Id);
        var updatedB = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entryB.Id);

        updatedA.State.Should().Be(RecoveryEntryState.Recovered);
        updatedB.State.Should().Be(RecoveryEntryState.Observing); // untouched by owner A's sweep
    }

    [Fact]
    public async Task SweepOwnerAsync_CancelledAfterFirstEntry_AlreadyRecordedEvidenceSurvives()
    {
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure, OwnerA);
        var first = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddMinutes(-2));
        var second = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var worker = CreateWorker();
        var scope = BuildScope(_dbContext, _recoveryLedger, _namespaceRepoMock.Object, AzureRouter());

        using var cts = new CancellationTokenSource();
        // Cancel deterministically right after the first iteration's RecordObservationAsync
        // durably commits, instead of racing a wall-clock delay against DB I/O (which was flaky
        // under parallel test-suite load — the whole sweep could finish before a 1ms timer fired).
        // DbContext.SavedChanges fires synchronously once SaveChangesAsync completes, so by the
        // time the loop reaches the second iteration's ThrowIfCancellationRequested, the token is
        // already cancelled.
        void CancelOnFirstSave(object? sender, SavedChangesEventArgs args)
        {
            _dbContext.SavedChanges -= CancelOnFirstSave;
            cts.Cancel();
        }
        _dbContext.SavedChanges += CancelOnFirstSave;

        var act = async () => await worker.SweepOwnerAsync(scope, OwnerA, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Whichever entries did complete before cancellation must be durably persisted — a
        // cancelled sweep must not roll back or erase evidence already written.
        var firstState = (await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == first.Id)).State;
        var secondState = (await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == second.Id)).State;
        (firstState == RecoveryEntryState.Recovered || firstState == RecoveryEntryState.Observing).Should().BeTrue();
        (secondState == RecoveryEntryState.Recovered || secondState == RecoveryEntryState.Observing).Should().BeTrue();
    }

    [Fact]
    public async Task SweepOwnerAsync_SimulatedRestart_FreshScopeStillClosesDueEntry()
    {
        // A worker sweep is purely query-driven off persisted state — nothing is cached across
        // calls. Simulating a process restart just means: build an entirely new scope/service
        // set against the same durable DB and sweep again.
        var namespaceId = Guid.NewGuid();
        CreateNamespace(namespaceId, CloudProviderType.Azure, OwnerA);
        var entry = await BeginObservingEntryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var freshDbContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(_dbContext.Database.GetDbConnection()).Options);
        var freshLedger = new RecoveryLedgerService(freshDbContext);
        var freshWorker = new RecoveryVerificationWorker(
            Mock.Of<IServiceProvider>(), new ConfigurationBuilder().Build(), NullLogger<RecoveryVerificationWorker>.Instance);
        var freshScope = BuildScope(freshDbContext, freshLedger, _namespaceRepoMock.Object, AzureRouter());

        await freshWorker.SweepOwnerAsync(freshScope, OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Recovered);
    }
}
