using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Covers <see cref="PillarFindingRetentionWorker"/>'s sweep logic (roadmap next-chapter M1.2,
/// ADR-0009) — in particular the exception this milestone exists to enforce: a finding a
/// <see cref="PlaybookEntry"/> still cites is never pruned, regardless of age.
/// </summary>
public sealed class PillarFindingRetentionWorkerTests : IDisposable
{
    private const string OwnerId = "owner-1";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;

    public PillarFindingRetentionWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static readonly DateTimeOffset Cutoff = DateTimeOffset.UtcNow.AddDays(-90);
    private static readonly DateTimeOffset Expired = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset Fresh = DateTimeOffset.UtcNow;

    private async Task<DriftFinding> SeedExpiredDriftFindingAsync()
    {
        var finding = DriftFinding.Create(Guid.NewGuid(), "test-queue", DriftFindingType.SchemaShapeDrift, 60, "drift");
        _dbContext.DriftFindings.Add(finding);
        _dbContext.Entry(finding).Property("OwnerId").CurrentValue = OwnerId;
        _dbContext.Entry(finding).Property(nameof(DriftFinding.DetectedAt)).CurrentValue = Expired;
        await _dbContext.SaveChangesAsync();
        return finding;
    }

    private async Task CitePlaybookEntryAsync(Guid driftFindingId)
    {
        _dbContext.PlaybookEntries.Add(new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionTrigger",
            EvidenceRefJson = $$"""{"DriftFindingId":"{{driftFindingId}}","RuleEntryId":"{{Guid.NewGuid()}}"}""",
            ProposalJson = "{}",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "system:test",
            ProposerKind = PlaybookActorKind.System,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SweepAsync_ExpiredUncitedDriftFinding_IsDeleted()
    {
        var finding = await SeedExpiredDriftFindingAsync();

        await PillarFindingRetentionWorker.SweepAsync(_dbContext, Cutoff, CancellationToken.None);

        (await _dbContext.DriftFindings.AsNoTracking().AnyAsync(f => f.Id == finding.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepAsync_ExpiredButCitedDriftFinding_Survives()
    {
        var finding = await SeedExpiredDriftFindingAsync();
        await CitePlaybookEntryAsync(finding.Id);

        await PillarFindingRetentionWorker.SweepAsync(_dbContext, Cutoff, CancellationToken.None);

        (await _dbContext.DriftFindings.AsNoTracking().AnyAsync(f => f.Id == finding.Id)).Should().BeTrue(
            "a finding a PlaybookEntry still cites must never be pruned, regardless of age");
    }

    [Fact]
    public async Task SweepAsync_CitedFindingSurvivesWhileUncitedSiblingIsDeleted()
    {
        // This is the exact scenario the roadmap names as the "done when" test for M1.2.
        var cited = await SeedExpiredDriftFindingAsync();
        var uncited = await SeedExpiredDriftFindingAsync();
        await CitePlaybookEntryAsync(cited.Id);

        await PillarFindingRetentionWorker.SweepAsync(_dbContext, Cutoff, CancellationToken.None);

        (await _dbContext.DriftFindings.AsNoTracking().AnyAsync(f => f.Id == cited.Id)).Should().BeTrue();
        (await _dbContext.DriftFindings.AsNoTracking().AnyAsync(f => f.Id == uncited.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepAsync_FreshFinding_SurvivesRegardlessOfCitation()
    {
        var finding = DriftFinding.Create(Guid.NewGuid(), "test-queue", DriftFindingType.SchemaShapeDrift, 60, "drift");
        _dbContext.DriftFindings.Add(finding);
        _dbContext.Entry(finding).Property("OwnerId").CurrentValue = OwnerId;
        _dbContext.Entry(finding).Property(nameof(DriftFinding.DetectedAt)).CurrentValue = Fresh;
        await _dbContext.SaveChangesAsync();

        await PillarFindingRetentionWorker.SweepAsync(_dbContext, Cutoff, CancellationToken.None);

        (await _dbContext.DriftFindings.AsNoTracking().AnyAsync(f => f.Id == finding.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task SweepAsync_SweepsAllSixTables()
    {
        var namespaceId = Guid.NewGuid();

        var anomaly = Anomaly.Create(namespaceId, "q", AnomalyType.HighMessageVolume, 50, "d");
        _dbContext.Anomalies.Add(anomaly);
        _dbContext.Entry(anomaly).Property("OwnerId").CurrentValue = OwnerId;
        _dbContext.Entry(anomaly).Property(nameof(Anomaly.DetectedAt)).CurrentValue = Expired;

        var drift = DriftFinding.Create(namespaceId, "q", DriftFindingType.SchemaShapeDrift, 50, "d");
        _dbContext.DriftFindings.Add(drift);
        _dbContext.Entry(drift).Property("OwnerId").CurrentValue = OwnerId;
        _dbContext.Entry(drift).Property(nameof(DriftFinding.DetectedAt)).CurrentValue = Expired;

        var correlation = CorrelationFinding.Create(
            OwnerId,
            [new CorrelationMember(namespaceId, "q", AnomalyType.HighMessageVolume, 50, CloudProviderType.Azure)],
            50, "d");
        _dbContext.CorrelationFindings.Add(correlation);
        _dbContext.Entry(correlation).Property(nameof(CorrelationFinding.DetectedAt)).CurrentValue = Expired;

        var narration = Narration.Create(NarrationKind.NamespaceActivity, namespaceId, [namespaceId], "h", "s", 50);
        _dbContext.Narrations.Add(narration);
        _dbContext.Entry(narration).Property(nameof(Narration.GeneratedAt)).CurrentValue = Expired;

        var forecast = BacklogForecast.Create(namespaceId, "q", 100, 5.0, 500, 10.0, 50, "d");
        _dbContext.BacklogForecasts.Add(forecast);
        _dbContext.Entry(forecast).Property("OwnerId").CurrentValue = OwnerId;
        _dbContext.Entry(forecast).Property(nameof(BacklogForecast.DetectedAt)).CurrentValue = Expired;

        var signal = new ExternalSignalEvent
        {
            OwnerId = OwnerId,
            NamespaceId = namespaceId,
            SignalType = ExternalSignalType.Deploy,
            OccurredAt = Expired,
            Source = "ci",
            IngestedAt = Expired,
        };
        var externalCorrelation = ExternalSignalCorrelation.Create(
            OwnerId, namespaceId, "q", AnomalyType.HighMessageVolume, 50, CloudProviderType.Azure,
            signal, TimeSpan.FromMinutes(1), "d");
        _dbContext.ExternalSignalCorrelations.Add(externalCorrelation);
        _dbContext.Entry(externalCorrelation).Property(nameof(ExternalSignalCorrelation.DetectedAt)).CurrentValue = Expired;

        await _dbContext.SaveChangesAsync();

        var deleted = await PillarFindingRetentionWorker.SweepAsync(_dbContext, Cutoff, CancellationToken.None);

        deleted.Should().ContainKeys("Anomalies", "DriftFindings", "CorrelationFindings", "Narrations", "BacklogForecasts", "ExternalSignalCorrelations");
        deleted.Values.Should().AllSatisfy(v => v.Should().Be(1));

        (await _dbContext.Anomalies.CountAsync()).Should().Be(0);
        (await _dbContext.DriftFindings.CountAsync()).Should().Be(0);
        (await _dbContext.CorrelationFindings.CountAsync()).Should().Be(0);
        (await _dbContext.Narrations.CountAsync()).Should().Be(0);
        (await _dbContext.BacklogForecasts.CountAsync()).Should().Be(0);
        (await _dbContext.ExternalSignalCorrelations.CountAsync()).Should().Be(0);
    }
}
