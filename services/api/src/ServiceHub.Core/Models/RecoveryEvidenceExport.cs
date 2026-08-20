namespace ServiceHub.Core.Models;

/// <summary>
/// The rendered artefacts of a Recovery Evidence Ledger export for one operation — canonical
/// JSON and CSV, computed once from durable ledger state. Two exports of the same, unchanged
/// operation produce byte-identical strings here except for <see cref="RecoveryEvidenceManifest.ExportedAt"/>
/// and <see cref="RecoveryEvidenceManifest.ExportedBy"/> inside <see cref="ManifestJson"/> and
/// <see cref="BundleJson"/> (roadmap §16.5). See <c>IRecoveryEvidenceExporter</c>.
/// </summary>
public sealed class RecoveryEvidenceExport
{
    /// <summary><c>manifest.json</c> — see <see cref="RecoveryEvidenceManifest"/>.</summary>
    public required string ManifestJson { get; init; }

    /// <summary><c>operation.json</c> — the operation header.</summary>
    public required string OperationJson { get; init; }

    /// <summary><c>entries.json</c> — every entry begun under this operation.</summary>
    public required string EntriesJson { get; init; }

    /// <summary><c>events.json</c> — the full, <c>Seq</c>-ordered event chain for this operation.</summary>
    public required string EventsJson { get; init; }

    /// <summary><c>entries.csv</c> — spreadsheet-friendly flattening of <see cref="EntriesJson"/>.</summary>
    public required string EntriesCsv { get; init; }

    /// <summary>
    /// Single-file JSON representation combining manifest, operation, entries and events — used
    /// for the plain <c>format=json</c> export (as opposed to <c>format=package</c>'s five-file
    /// zip). Not a re-serialization of the other fields' text; built from the same underlying data
    /// once, so it stays exactly consistent with them.
    /// </summary>
    public required string BundleJson { get; init; }
}
