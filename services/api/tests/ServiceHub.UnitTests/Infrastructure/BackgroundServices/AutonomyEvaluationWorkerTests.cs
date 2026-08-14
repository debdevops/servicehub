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

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Phase C coverage for <see cref="AutonomyEvaluationWorker"/>: the sweep finds every signature
/// with replay evidence and evaluates it read-only — no ledger write, no <c>AutonomyGrant</c>
/// (that write path is Phase D's).
/// </summary>
public sealed class AutonomyEvaluationWorkerTests : IDisposable
{
    private const string OwnerA = "owner-a";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly RecoveryTrustScoringService _trustScoring;

    public AutonomyEvaluationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
        _trustScoring = new RecoveryTrustScoringService(_recoveryLedger);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static AutonomyEvaluationWorker CreateWorker() =>
        new(
            Mock.Of<IServiceProvider>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<AutonomyEvaluationWorker>.Instance);

    private IServiceProvider BuildScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRecoveryLedger>(_recoveryLedger);
        services.AddSingleton<IRecoveryTrustScoringService>(_trustScoring);
        return services.BuildServiceProvider();
    }

    private async Task CreateRecoveredReplayEntryAsync(string signatureHash, string bodyHash)
    {
        var operation = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = OwnerA,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = bodyHash,
            SignatureHashSnapshot = signatureHash,
            TargetEntity = "orders-dlq",
        });

        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Value.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("verification-worker", RecoveryActorKind.System),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });
    }

    [Fact]
    public async Task SweepOwnerAsync_NoSignatureEvidence_CompletesWithoutError()
    {
        var worker = CreateWorker();
        var act = () => worker.SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SweepOwnerAsync_SignatureWithEvidence_DoesNotMutateTheLedger()
    {
        await CreateRecoveredReplayEntryAsync("sig-hash-1", "body-1");

        var entryCountBefore = await _dbContext.RecoveryLedgerEntries.CountAsync();
        var eventCountBefore = await _dbContext.RecoveryEvents.CountAsync();

        var worker = CreateWorker();
        await worker.SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.RecoveryLedgerEntries.CountAsync()).Should().Be(entryCountBefore);
        (await _dbContext.RecoveryEvents.CountAsync()).Should().Be(eventCountBefore);
    }
}
