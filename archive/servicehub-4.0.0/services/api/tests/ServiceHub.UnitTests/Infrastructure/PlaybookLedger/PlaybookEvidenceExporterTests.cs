using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

/// <summary>
/// Covers <see cref="PlaybookEvidenceExporter"/> (roadmap next-chapter M1.3, ADR-0009) — the
/// export that closes exit statement 9 for the Investigate/Correlate/Prevent pillars: an auditor
/// holding only the export must be able to resolve every <c>EvidenceRefJson</c> citation without
/// server access.
/// </summary>
public sealed class PlaybookEvidenceExporterTests : IDisposable
{
    private const string OwnerId = "owner-1";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly PlaybookEvidenceExporter _sut;

    public PlaybookEvidenceExporterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();

        _playbookLedger = new ServiceHub.Infrastructure.PlaybookLedger.PlaybookLedgerService(_dbContext);
        _sut = new PlaybookEvidenceExporter(
            _playbookLedger,
            new SqliteAnomalyResultCache(_dbContext),
            new SqliteDriftResultCache(_dbContext),
            new SqliteCorrelationResultCache(_dbContext),
            new SqliteExternalSignalCorrelationCache(_dbContext));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private async Task<Guid> ProposeEntryAsync(string evidenceRefJson)
    {
        var result = await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionTrigger",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = "{}",
            Proposer = new ServiceHub.Core.Models.PlaybookActor("system:test", PlaybookActorKind.System),
            ExpiresAfter = TimeSpan.FromDays(30),
        });
        return result.Value.Id;
    }

    [Fact]
    public async Task ExportAsync_EntryCitingLiveDriftFinding_ResolvesItInline()
    {
        var finding = DriftFinding.Create(Guid.NewGuid(), "test-queue", DriftFindingType.SchemaShapeDrift, 60, "shape drift");
        _dbContext.DriftFindings.Add(finding);
        _dbContext.Entry(finding).Property("OwnerId").CurrentValue = OwnerId;
        await _dbContext.SaveChangesAsync();

        await ProposeEntryAsync($$"""{"DriftFindingId":"{{finding.Id}}","RuleEntryId":"{{Guid.NewGuid()}}"}""");

        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        export.DanglingCitations.Should().BeEmpty();
        var citedEvidence = JsonDocument.Parse(export.CitedEvidenceJson);
        citedEvidence.RootElement.TryGetProperty(finding.Id.ToString(), out var resolved).Should().BeTrue();
        resolved.GetProperty("description").GetString().Should().Be("shape drift");
    }

    [Fact]
    public async Task ExportAsync_EntryCitingAFindingThatWasSincePruned_IsReportedAsDangling()
    {
        // Simulates the exact scenario M1.2's retention exception exists to prevent — this test
        // proves the export/verify side would actually catch it if it ever did happen.
        var neverStoredFindingId = Guid.NewGuid();
        await ProposeEntryAsync($$"""{"DriftFindingId":"{{neverStoredFindingId}}","RuleEntryId":"{{Guid.NewGuid()}}"}""");

        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        export.DanglingCitations.Should().ContainSingle(c => c.FindingId == neverStoredFindingId && c.FieldName == "DriftFindingId");
    }

    [Fact]
    public async Task ExportAsync_EntryCitingNonFindingData_IsNeitherResolvedNorDangling()
    {
        // A ReplayPlan/manual-proposal-shaped evidence ref (RuleId, MessageId, etc.) is not a
        // finding citation at all — it must not be flagged as dangling.
        await ProposeEntryAsync("""{"MessageId":"abc-123","SequenceNumber":42}""");

        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        export.DanglingCitations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_ResolvesAllFourFindingKinds()
    {
        var namespaceId = Guid.NewGuid();

        var anomaly = Anomaly.Create(namespaceId, "q", AnomalyType.HighMessageVolume, 50, "anomaly-desc");
        _dbContext.Anomalies.Add(anomaly);
        _dbContext.Entry(anomaly).Property("OwnerId").CurrentValue = OwnerId;

        var correlation = CorrelationFinding.Create(
            OwnerId, [new CorrelationMember(namespaceId, "q", AnomalyType.HighMessageVolume, 50, CloudProviderType.Azure)],
            50, "correlation-desc");
        _dbContext.CorrelationFindings.Add(correlation);

        var signal = new ExternalSignalEvent
        {
            OwnerId = OwnerId, NamespaceId = namespaceId, SignalType = ExternalSignalType.Deploy,
            OccurredAt = DateTimeOffset.UtcNow, Source = "ci", IngestedAt = DateTimeOffset.UtcNow,
        };
        var externalCorrelation = ExternalSignalCorrelation.Create(
            OwnerId, namespaceId, "q", AnomalyType.HighMessageVolume, 50, CloudProviderType.Azure,
            signal, TimeSpan.FromMinutes(1), "external-desc");
        _dbContext.ExternalSignalCorrelations.Add(externalCorrelation);

        await _dbContext.SaveChangesAsync();

        await ProposeEntryAsync($$"""{"AnomalyId":"{{anomaly.Id}}"}""");
        await ProposeEntryAsync($$"""{"CorrelationFindingId":"{{correlation.Id}}"}""");
        await ProposeEntryAsync($$"""{"ExternalSignalCorrelationId":"{{externalCorrelation.Id}}"}""");

        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        export.DanglingCitations.Should().BeEmpty();
        var citedEvidence = JsonDocument.Parse(export.CitedEvidenceJson);
        citedEvidence.RootElement.TryGetProperty(anomaly.Id.ToString(), out _).Should().BeTrue();
        citedEvidence.RootElement.TryGetProperty(correlation.Id.ToString(), out _).Should().BeTrue();
        citedEvidence.RootElement.TryGetProperty(externalCorrelation.Id.ToString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_NoEntries_ReturnsEmptyBundleWithoutThrowing()
    {
        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        export.DanglingCitations.Should().BeEmpty();
        JsonDocument.Parse(export.EntriesJson).RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExportAsync_BundleJsonContainsManifestEntriesEventsAndCitedEvidence()
    {
        var finding = DriftFinding.Create(Guid.NewGuid(), "q", DriftFindingType.SchemaShapeDrift, 40, "d");
        _dbContext.DriftFindings.Add(finding);
        _dbContext.Entry(finding).Property("OwnerId").CurrentValue = OwnerId;
        await _dbContext.SaveChangesAsync();
        await ProposeEntryAsync($$"""{"DriftFindingId":"{{finding.Id}}"}""");

        var export = await _sut.ExportAsync(OwnerId, "test-exporter");

        var bundle = JsonDocument.Parse(export.BundleJson).RootElement;
        bundle.TryGetProperty("manifest", out _).Should().BeTrue();
        bundle.TryGetProperty("entries", out _).Should().BeTrue();
        bundle.TryGetProperty("events", out _).Should().BeTrue();
        bundle.TryGetProperty("citedEvidence", out _).Should().BeTrue();
    }
}
