using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

/// <summary>
/// Phase 5 (Export) coverage for <see cref="RecoveryEvidenceExporter"/>: the manifest's honesty
/// contract (roadmap §16.3) and byte-reproducibility (§16.5).
/// </summary>
public sealed class RecoveryEvidenceExporterTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly RecoveryEvidenceExporter _exporter;

    public RecoveryEvidenceExporterTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
        _exporter = new RecoveryEvidenceExporter(_recoveryLedger);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor(string identity = "test-actor") => new(identity, RecoveryActorKind.User);

    private async Task<RecoveryOperation> OpenOperationAsync(string ownerId = OwnerA)
    {
        var result = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(
        RecoveryOperation operation, CloudProviderType? provider = null, string bodyHash = "hash-1")
    {
        var result = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = operation.OwnerId,
            Actor = Actor(),
            ProviderSnapshot = provider,
            EntityNameSnapshot = "orders-dlq",
            BodyHash = bodyHash,
            TargetEntity = "orders-dlq",
        });
        return result.Value;
    }

    [Fact]
    public async Task ExportAsync_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerB);

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ExportAsync_WhatServiceHubDoesNotKnow_IsNeverEmpty()
    {
        var operation = await OpenOperationAsync();

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        result.IsSuccess.Should().BeTrue();
        result.Value.ManifestJson.Should().Contain("whatServiceHubDoesNotKnow");
        result.Value.ManifestJson.Should().NotContain("\"whatServiceHubDoesNotKnow\": []");
        result.Value.ManifestJson.Should().NotContain("\"whatServiceHubDoesNotKnow\":[]");
    }

    [Fact]
    public async Task ExportAsync_ManifestDeclaresTamperEvidentNotTamperProof()
    {
        var operation = await OpenOperationAsync();

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        result.Value.ManifestJson.Should().Contain("\"tamperEvidentNotTamperProof\": true");
    }

    [Fact]
    public async Task ExportAsync_IntactChain_ManifestReportsVerifiedTrue()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation);

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        result.Value.ManifestJson.Should().Contain("\"verified\": true");
    }

    [Fact]
    public async Task ExportAsync_NoOutputContainsAConnectionStringOrCredentialLookingField()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation);

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");
        var export = result.Value;

        foreach (var doc in new[] { export.ManifestJson, export.OperationJson, export.EntriesJson, export.EventsJson, export.BundleJson })
        {
            doc.Should().NotContainEquivalentOf("connectionString");
            doc.Should().NotContainEquivalentOf("credential");
            doc.Should().NotContainEquivalentOf("password");
            doc.Should().NotContainEquivalentOf("bodyPreview");
        }
    }

    [Fact]
    public async Task ExportAsync_CountsIncludeEveryEntryState_EvenZero()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation); // stays Executing

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        // JsonSerializerOptions.PropertyNamingPolicy does not apply to Dictionary<string,_> keys
        // (that needs the separate DictionaryKeyPolicy, unset here), so the Counts keys serialize
        // as the raw enum names.
        foreach (var state in Enum.GetNames<RecoveryEntryState>())
        {
            result.Value.ManifestJson.Should().Contain($"\"{state}\"");
        }
    }

    [Fact]
    public async Task ExportAsync_AwsEntryUnverified_AddsAwsNoAbsenceProofLimitation()
    {
        var operation = await OpenOperationAsync();
        var entry = await BeginEntryAsync(operation, CloudProviderType.Aws);
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.ObservationUnavailable,
        });

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        result.Value.ManifestJson.Should().Contain("AWS_NO_ABSENCE_PROOF");
    }

    [Fact]
    public async Task ExportAsync_EntriesCsv_HasHeaderAndOneRowPerEntry()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation, bodyHash: "hash-a");
        await BeginEntryAsync(operation, bodyHash: "hash-b");

        var result = await _exporter.ExportAsync(operation.Id, OwnerA, "test-exporter");

        var lines = result.Value.EntriesCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3); // header + 2 entries
        lines[0].Should().StartWith("Id,");
    }

    [Fact]
    public async Task ExportAsync_TwoConsecutiveExports_BundleJsonByteIdenticalExceptExportedAt()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation);

        var first = (await _exporter.ExportAsync(operation.Id, OwnerA, "same-actor")).Value;
        var second = (await _exporter.ExportAsync(operation.Id, OwnerA, "same-actor")).Value;

        var normalizedFirst = System.Text.RegularExpressions.Regex.Replace(
            first.BundleJson, "\"exportedAt\":\\s*\"[^\"]*\"", "\"exportedAt\": \"REDACTED\"");
        var normalizedSecond = System.Text.RegularExpressions.Regex.Replace(
            second.BundleJson, "\"exportedAt\":\\s*\"[^\"]*\"", "\"exportedAt\": \"REDACTED\"");

        normalizedFirst.Should().Be(normalizedSecond);
    }

    [Fact]
    public async Task ExportAsync_DifferentExportedBy_OnlyThatFieldDiffers()
    {
        var operation = await OpenOperationAsync();
        await BeginEntryAsync(operation);

        var asAlice = (await _exporter.ExportAsync(operation.Id, OwnerA, "alice")).Value;
        var asBob = (await _exporter.ExportAsync(operation.Id, OwnerA, "bob")).Value;

        asAlice.ManifestJson.Should().Contain("\"exportedBy\": \"alice\"");
        asBob.ManifestJson.Should().Contain("\"exportedBy\": \"bob\"");

        var normalizedAlice = StripVolatileManifestFields(asAlice.BundleJson);
        var normalizedBob = StripVolatileManifestFields(asBob.BundleJson);
        normalizedAlice.Should().Be(normalizedBob);
    }

    private static string StripVolatileManifestFields(string json)
    {
        var noExportedAt = System.Text.RegularExpressions.Regex.Replace(
            json, "\"exportedAt\":\\s*\"[^\"]*\"", "\"exportedAt\": \"REDACTED\"");
        return System.Text.RegularExpressions.Regex.Replace(
            noExportedAt, "\"exportedBy\":\\s*\"[^\"]*\"", "\"exportedBy\": \"REDACTED\"");
    }
}
