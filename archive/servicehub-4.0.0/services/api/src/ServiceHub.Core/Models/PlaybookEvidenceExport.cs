namespace ServiceHub.Core.Models;

/// <summary>
/// One <see cref="Entities.PlaybookEntry.EvidenceRefJson"/> citation into a pillar-finding table
/// (roadmap next-chapter M1.3, ADR-0009) — <c>fieldName</c> is one of the four the detection
/// workers write (<c>AnomalyId</c>, <c>DriftFindingId</c>, <c>CorrelationFindingId</c>,
/// <c>ExternalSignalCorrelationId</c>), and <c>findingId</c> is the value cited.
/// </summary>
public sealed record PlaybookEvidenceCitation(string FieldName, Guid FindingId);

/// <summary>
/// The rendered artefact of a Playbook Ledger evidence export for one owner (roadmap next-chapter
/// M1.3, ADR-0009) — every entry, its full hash-chained event log, and every pillar finding any
/// entry's <c>EvidenceRefJson</c> cites, resolved and inlined by value. This is what closes exit
/// statement 9 for the three pillars whose evidence previously lived in a 24-hour, process-local
/// cache: an auditor holding only this export can resolve every citation without server access,
/// even after retention (<c>PillarFindingRetentionWorker</c>) has since pruned the live row.
/// </summary>
public sealed class PlaybookEvidenceExport
{
    /// <summary><c>manifest.json</c> — schema version, chain verification summary, and the list
    /// of any citations that could not be resolved (see <see cref="DanglingCitations"/>).</summary>
    public required string ManifestJson { get; init; }

    /// <summary><c>entries.json</c> — every Playbook entry for this owner.</summary>
    public required string EntriesJson { get; init; }

    /// <summary><c>events.json</c> — the full, <c>Seq</c>-ordered Playbook event chain.</summary>
    public required string EventsJson { get; init; }

    /// <summary>
    /// <c>cited-evidence.json</c> — every successfully resolved pillar finding any entry cites,
    /// keyed by its own id (as a string), serialized as its own entity shape verbatim. An auditor
    /// resolves a citation by looking up <see cref="PlaybookEvidenceCitation.FindingId"/> here.
    /// </summary>
    public required string CitedEvidenceJson { get; init; }

    /// <summary>
    /// Single-file JSON combining manifest, entries, events and cited evidence — the
    /// <c>format=json</c> export. Built from the same underlying data as the fields above, once.
    /// </summary>
    public required string BundleJson { get; init; }

    /// <summary>
    /// Citations that named a finding id no longer resolvable — a defect if it happens
    /// (<c>PillarFindingRetentionWorker</c> exists specifically so it shouldn't), never expected in
    /// a healthy export. Also present in <see cref="ManifestJson"/>.
    /// </summary>
    public required IReadOnlyList<PlaybookEvidenceCitation> DanglingCitations { get; init; }
}
