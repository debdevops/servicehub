using System.Text;
using System.Text.Json;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// EF Core-backed implementation of <see cref="IRecoveryEvidenceExporter"/>. Reads exclusively
/// through <see cref="IRecoveryLedger"/> — it never touches <c>DlqDbContext</c> directly — so it
/// carries no ability to write, and its "what happened" account can never diverge from what the
/// ledger itself would answer to the same queries.
/// </summary>
public sealed class RecoveryEvidenceExporter : IRecoveryEvidenceExporter
{
    private const int ManifestSchemaVersion = 1;
    private const int MaxExportEntries = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] WhatServiceHubKnows =
    [
        "Who initiated each recovery decision and under what stated reason",
        "What ServiceHub asked the provider to do, and the provider's response",
        "The exact scope, actor, and intent header presented for the decision",
    ];

    private static readonly string[] WhatServiceHubObserved =
    [
        "Whether a message carrying this operation's recovery marker re-entered the dead-letter queue",
        "Whether a message matching a replayed message's body hash re-entered the dead-letter queue, when no marker could be applied",
        "The scan coverage over each entry's observation window",
    ];

    private static readonly string[] WhatServiceHubDoesNotKnow =
    [
        "Whether any consumer processed the message successfully",
        "Whether the corresponding business transaction completed",
        "Anything about messages removed by systems other than ServiceHub",
        "For entries with markerApplied=false, which specific message a body-hash match refers to",
    ];

    private readonly IRecoveryLedger _recoveryLedger;

    /// <summary>Initialises a new instance of <see cref="RecoveryEvidenceExporter"/>.</summary>
    public RecoveryEvidenceExporter(IRecoveryLedger recoveryLedger)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryEvidenceExport>> ExportAsync(
        Guid operationId, string ownerId, string exportedBy, CancellationToken cancellationToken = default)
    {
        var operation = await _recoveryLedger.GetOperationAsync(operationId, ownerId, cancellationToken);
        if (operation is null)
        {
            return Result<RecoveryEvidenceExport>.Failure(Error.NotFound(
                "RecoveryEvidenceExporter.OperationNotFound", "Recovery operation not found."));
        }

        var rawEntries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = ownerId,
            OperationId = operationId,
            Limit = MaxExportEntries,
        }, cancellationToken);

        // Deterministic order independent of any database-level tie-breaking: reproducibility
        // (roadmap §16.5) requires the same input to always serialize to the same bytes.
        var entries = rawEntries.OrderBy(e => e.BegunAt).ThenBy(e => e.Id).ToList();

        var events = await _recoveryLedger.GetEventsForOperationAsync(operationId, ownerId, cancellationToken);
        var chainResult = await _recoveryLedger.VerifyChainAsync(ownerId, cancellationToken);

        var operationResponse = MapOperation(operation);
        var entryResponses = entries.Select(MapEntry).ToList();
        var eventResponses = events.Select(MapEvent).ToList();

        var manifest = BuildManifest(operation, entries, events, chainResult, ownerId, exportedBy);

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var operationJson = JsonSerializer.Serialize(operationResponse, JsonOptions);
        var entriesJson = JsonSerializer.Serialize(entryResponses, JsonOptions);
        var eventsJson = JsonSerializer.Serialize(eventResponses, JsonOptions);
        var entriesCsv = BuildEntriesCsv(entries);
        var bundleJson = JsonSerializer.Serialize(new
        {
            manifest,
            operation = operationResponse,
            entries = entryResponses,
            events = eventResponses,
        }, JsonOptions);

        return Result<RecoveryEvidenceExport>.Success(new RecoveryEvidenceExport
        {
            ManifestJson = manifestJson,
            OperationJson = operationJson,
            EntriesJson = entriesJson,
            EventsJson = eventsJson,
            EntriesCsv = entriesCsv,
            BundleJson = bundleJson,
        });
    }

    private static RecoveryEvidenceManifest BuildManifest(
        RecoveryOperation operation,
        IReadOnlyList<RecoveryLedgerEntry> entries,
        IReadOnlyList<RecoveryEvent> events,
        ChainVerificationResult chainResult,
        string ownerId,
        string exportedBy)
    {
        var counts = Enum.GetValues<RecoveryEntryState>()
            .ToDictionary(s => s.ToString(), s => entries.Count(e => e.State == s));

        var firstSeq = events.Count > 0 ? events.Min(e => e.Seq) : 0;
        var lastSeq = events.Count > 0 ? events.Max(e => e.Seq) : 0;

        return new RecoveryEvidenceManifest
        {
            SchemaVersion = ManifestSchemaVersion,
            ServiceVersion = GetServiceVersion(),
            ExportedAt = DateTimeOffset.UtcNow,
            ExportedBy = exportedBy,
            OwnerId = ownerId,
            OperationId = operation.Id,
            Chain = new RecoveryEvidenceChainSummary
            {
                Scope = "owner",
                FirstSeq = firstSeq,
                LastSeq = lastSeq,
                Verified = chainResult.IsValid,
                Algorithm = "SHA-256 over canonical JSON with PrevHash",
                TamperEvidentNotTamperProof = true,
            },
            Counts = counts,
            WhatServiceHubKnows = WhatServiceHubKnows,
            WhatServiceHubObserved = WhatServiceHubObserved,
            WhatServiceHubDoesNotKnow = WhatServiceHubDoesNotKnow,
            Limitations = BuildLimitations(entries),
        };
    }

    private static IReadOnlyList<RecoveryEvidenceLimitation> BuildLimitations(IReadOnlyList<RecoveryLedgerEntry> entries)
    {
        var limitations = new List<RecoveryEvidenceLimitation>();

        var awsUnverified = entries.Count(e =>
            e.ProviderSnapshot == CloudProviderType.Aws && e.VerificationResult == VerificationResult.Unverified);
        if (awsUnverified > 0)
        {
            limitations.Add(new RecoveryEvidenceLimitation
            {
                Code = "AWS_NO_ABSENCE_PROOF",
                AppliesToEntries = awsUnverified,
                Text = "AWS SQS has no non-destructive peek; ServiceHub cannot prove a message did not return.",
            });
        }

        var gcpUnverified = entries.Count(e =>
            e.ProviderSnapshot == CloudProviderType.Gcp && e.VerificationResult == VerificationResult.Unverified);
        if (gcpUnverified > 0)
        {
            limitations.Add(new RecoveryEvidenceLimitation
            {
                Code = "GCP_NO_ABSENCE_PROOF",
                AppliesToEntries = gcpUnverified,
                Text = "GCP Pub/Sub scanning is capped per cycle and reconciliation is skipped once the cap is hit; ServiceHub cannot prove a message did not return.",
            });
        }

        var markerNotApplied = entries.Count(e => !e.MarkerApplied && e.SourceMessageIdSnapshot is not null);
        if (markerNotApplied > 0)
        {
            limitations.Add(new RecoveryEvidenceLimitation
            {
                Code = "MARKER_NOT_APPLIED",
                AppliesToEntries = markerNotApplied,
                Text = "The recovery marker could not be applied to this message. Any recurrence match for it is a body-hash heuristic, not an exact attribution, and may be shared with other open entries.",
            });
        }

        return limitations;
    }

    private static string BuildEntriesCsv(IReadOnlyList<RecoveryLedgerEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,NamespaceName,Provider,Environment,Entity,BodyHash,TargetEntity,BegunAt,MarkerApplied,State,Disposition,VerificationResult,VerificationConfidence,ClosedAt");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                e.Id.ToString(),
                EscapeCsv(e.NamespaceNameSnapshot ?? string.Empty),
                EscapeCsv(e.ProviderSnapshot?.ToString() ?? string.Empty),
                EscapeCsv(e.EnvironmentSnapshot?.ToString() ?? string.Empty),
                EscapeCsv(e.EntityNameSnapshot ?? string.Empty),
                EscapeCsv(e.BodyHash),
                EscapeCsv(e.TargetEntity),
                e.BegunAt.ToString("o"),
                e.MarkerApplied.ToString(),
                EscapeCsv(e.State.ToString()),
                EscapeCsv(e.Disposition?.ToString() ?? string.Empty),
                EscapeCsv(e.VerificationResult?.ToString() ?? string.Empty),
                EscapeCsv(e.VerificationConfidence?.ToString() ?? string.Empty),
                e.ClosedAt?.ToString("o") ?? string.Empty));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Neutralise spreadsheet formula injection: a leading =, +, -, @, tab or CR would
        // otherwise be evaluated as a formula when the CSV is opened in Excel.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static RecoveryOperationResponse MapOperation(RecoveryOperation operation) => new(
        Id: operation.Id,
        Kind: operation.Kind.ToString(),
        Trigger: operation.Trigger.ToString(),
        ActorIdentity: operation.ActorIdentity,
        ActorKind: operation.ActorKind.ToString(),
        Reason: operation.Reason,
        NamespaceId: operation.NamespaceId,
        NamespaceNameSnapshot: operation.NamespaceNameSnapshot,
        ProviderSnapshot: operation.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: operation.EnvironmentSnapshot?.ToString(),
        ScopeDescription: operation.ScopeDescription,
        SourceRuleId: operation.SourceRuleId,
        SourceJobId: operation.SourceJobId,
        ServiceVersion: operation.ServiceVersion,
        OpenedAt: operation.OpenedAt,
        TargetCount: operation.TargetCount);

    private static RecoveryLedgerEntryResponse MapEntry(RecoveryLedgerEntry entry) => new(
        Id: entry.Id,
        OperationId: entry.OperationId,
        DlqMessageId: entry.DlqMessageId,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        EntityNameSnapshot: entry.EntityNameSnapshot,
        EntityTypeSnapshot: entry.EntityTypeSnapshot,
        TopicNameSnapshot: entry.TopicNameSnapshot,
        BodyHash: entry.BodyHash,
        FailureCategorySnapshot: entry.FailureCategorySnapshot?.ToString(),
        DeadLetterReasonSnapshot: entry.DeadLetterReasonSnapshot,
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        TargetEntity: entry.TargetEntity,
        BegunAt: entry.BegunAt,
        MarkerApplied: entry.MarkerApplied,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        VerificationResult: entry.VerificationResult?.ToString(),
        VerificationConfidence: entry.VerificationConfidence?.ToString(),
        ObservationWindowEndsAt: entry.ObservationWindowEndsAt,
        ClosedAt: entry.ClosedAt);

    private static RecoveryEventResponse MapEvent(RecoveryEvent evt) => new(
        Id: evt.Id,
        OwnerId: evt.OwnerId,
        Seq: evt.Seq,
        EntryId: evt.EntryId,
        OperationId: evt.OperationId,
        EventType: evt.EventType.ToString(),
        OccurredAt: evt.OccurredAt,
        ActorIdentity: evt.ActorIdentity,
        ActorKind: evt.ActorKind.ToString(),
        DetailJson: evt.DetailJson,
        PrevHash: evt.PrevHash,
        EntryHash: evt.EntryHash,
        SchemaVersion: evt.SchemaVersion);

    private static string GetServiceVersion()
        => typeof(RecoveryEvidenceExporter).Assembly.GetName().Version?.ToString() ?? "unknown";
}
