using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// EF Core-backed implementation of <see cref="IPlaybookEvidenceExporter"/>. Reads entries and
/// events exclusively through <see cref="IPlaybookLedger"/> and resolves each citation through the
/// same four result-cache interfaces the detection workers already write through
/// (<see cref="IAnomalyResultCache"/>, <see cref="IDriftResultCache"/>,
/// <see cref="ICorrelationResultCache"/>, <see cref="IExternalSignalCorrelationCache"/>) — never a
/// direct <c>DlqDbContext</c> dependency, mirroring <c>RecoveryEvidenceExporter</c>'s own
/// read-only discipline.
/// </summary>
public sealed class PlaybookEvidenceExporter : IPlaybookEvidenceExporter
{
    private const int ManifestSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Entries/events are serialized directly from the domain entities (no mapping DTO, unlike
        // RecoveryEvidenceExporter's Response records) — without this, System.Text.Json's default
        // numeric enum encoding would silently diverge from PlaybookHashChain.ComputeEntryHash's
        // canonical form, which joins every enum field via ToString() (e.g. "Proposed", not "0").
        // An offline verifier recomputing the hash from a numerically-encoded export would never
        // reproduce a matching digest.
        Converters = { new JsonStringEnumConverter() },
    };

    // The exact field names the detection workers write into EvidenceRefJson when citing a
    // pillar finding (AnomalyDetectionWorker, DriftDetectionWorker, CorrelationDetectionWorker,
    // ExternalSignalCorrelationWorker, and PreventionRuleEvaluationService for DriftFindingId) —
    // never a namespace/naming convention, an explicit closed set so a field that merely looks
    // like an id (RuleId, MessageId) is never mistaken for a finding citation.
    private const string AnomalyIdField = "AnomalyId";
    private const string DriftFindingIdField = "DriftFindingId";
    private const string CorrelationFindingIdField = "CorrelationFindingId";
    private const string ExternalSignalCorrelationIdField = "ExternalSignalCorrelationId";

    private readonly IPlaybookLedger _playbookLedger;
    private readonly IAnomalyResultCache _anomalyCache;
    private readonly IDriftResultCache _driftCache;
    private readonly ICorrelationResultCache _correlationCache;
    private readonly IExternalSignalCorrelationCache _externalSignalCorrelationCache;

    /// <summary>Initialises a new instance of <see cref="PlaybookEvidenceExporter"/>.</summary>
    public PlaybookEvidenceExporter(
        IPlaybookLedger playbookLedger,
        IAnomalyResultCache anomalyCache,
        IDriftResultCache driftCache,
        ICorrelationResultCache correlationCache,
        IExternalSignalCorrelationCache externalSignalCorrelationCache)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _anomalyCache = anomalyCache ?? throw new ArgumentNullException(nameof(anomalyCache));
        _driftCache = driftCache ?? throw new ArgumentNullException(nameof(driftCache));
        _correlationCache = correlationCache ?? throw new ArgumentNullException(nameof(correlationCache));
        _externalSignalCorrelationCache = externalSignalCorrelationCache
            ?? throw new ArgumentNullException(nameof(externalSignalCorrelationCache));
    }

    /// <inheritdoc />
    public async Task<PlaybookEvidenceExport> ExportAsync(
        string ownerId, string exportedBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var entriesResult = await _playbookLedger.QueryEntriesAsync(ownerId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var entries = entriesResult.IsSuccess ? entriesResult.Value : [];

        var events = await _playbookLedger.GetAllEventsAsync(ownerId, cancellationToken).ConfigureAwait(false);
        var chainResult = await _playbookLedger.VerifyChainAsync(ownerId, cancellationToken).ConfigureAwait(false);

        var citations = entries
            .Select(e => (Entry: e, Citation: ExtractCitation(e.EvidenceRefJson)))
            .Where(x => x.Citation is not null)
            .Select(x => x.Citation!)
            .DistinctBy(c => (c.FieldName, c.FindingId))
            .ToList();

        var citedEvidence = new Dictionary<string, object>();
        var dangling = new List<PlaybookEvidenceCitation>();

        foreach (var citation in citations)
        {
            object? finding = citation.FieldName switch
            {
                AnomalyIdField => await _anomalyCache.TryGetAsync(citation.FindingId, cancellationToken).ConfigureAwait(false),
                DriftFindingIdField => await _driftCache.TryGetAsync(citation.FindingId, cancellationToken).ConfigureAwait(false),
                CorrelationFindingIdField => await _correlationCache.TryGetAsync(citation.FindingId, cancellationToken).ConfigureAwait(false),
                ExternalSignalCorrelationIdField => await _externalSignalCorrelationCache.TryGetAsync(citation.FindingId, cancellationToken).ConfigureAwait(false),
                _ => null,
            };

            if (finding is not null)
            {
                citedEvidence[citation.FindingId.ToString()] = finding;
            }
            else
            {
                dangling.Add(citation);
            }
        }

        var manifest = new
        {
            SchemaVersion = ManifestSchemaVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            ExportedBy = exportedBy,
            OwnerId = ownerId,
            Chain = new
            {
                Scope = "owner",
                EventsChecked = chainResult.EventsChecked,
                Verified = chainResult.IsValid,
                FirstDivergentSeq = chainResult.FirstDivergentSeq,
            },
            EntryCount = entries.Count,
            CitationCount = citations.Count,
            ResolvedCitationCount = citedEvidence.Count,
            DanglingCitations = dangling,
        };

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var entriesJson = JsonSerializer.Serialize(entries, JsonOptions);
        var eventsJson = JsonSerializer.Serialize(events, JsonOptions);
        var citedEvidenceJson = JsonSerializer.Serialize(citedEvidence, JsonOptions);
        var bundleJson = JsonSerializer.Serialize(new
        {
            manifest,
            entries,
            events,
            citedEvidence,
        }, JsonOptions);

        return new PlaybookEvidenceExport
        {
            ManifestJson = manifestJson,
            EntriesJson = entriesJson,
            EventsJson = eventsJson,
            CitedEvidenceJson = citedEvidenceJson,
            BundleJson = bundleJson,
            DanglingCitations = dangling,
        };
    }

    /// <summary>
    /// Parses <paramref name="evidenceRefJson"/> looking for exactly one of the four known
    /// finding-citation field names at the top level. Returns <c>null</c> when the evidence cites
    /// something else entirely (a message id, a rule id, a lifecycle snapshot — see
    /// <c>ReasoningCompanionWorker</c>/<c>AutoReplayExecutor</c>/<c>PreventionRulesController</c>'s
    /// own <c>evidenceRefJson</c> shapes), which is the common case and not a defect.
    /// </summary>
    private static PlaybookEvidenceCitation? ExtractCitation(string evidenceRefJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(evidenceRefJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in new[]
                     {
                         AnomalyIdField, DriftFindingIdField, CorrelationFindingIdField, ExternalSignalCorrelationIdField,
                     })
            {
                if (document.RootElement.TryGetProperty(fieldName, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(value.GetString(), out var id))
                {
                    return new PlaybookEvidenceCitation(fieldName, id);
                }
            }

            return null;
        }
    }
}
