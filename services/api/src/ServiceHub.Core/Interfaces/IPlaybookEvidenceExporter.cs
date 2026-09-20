using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Builds a Playbook Ledger evidence export for one owner — every entry, its full hash-chained
/// event log, and every pillar finding any entry's <c>EvidenceRefJson</c> cites, resolved and
/// inlined by value (roadmap next-chapter M1.3, ADR-0009). See
/// <see cref="Models.PlaybookEvidenceExport"/>.
/// </summary>
public interface IPlaybookEvidenceExporter
{
    /// <summary>
    /// Builds the export for one owner's entire Playbook Ledger.
    /// </summary>
    /// <param name="ownerId">The caller's owner ID — every read is owner-scoped.</param>
    /// <param name="exportedBy">Server-derived identity of the caller requesting the export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="allowedNamespaceIds">
    /// The caller's credential's namespace allow-list, when one is present — null means
    /// unrestricted. When set, the exported entries/events/cited-evidence are narrowed to entries
    /// within the allow-list (see <c>IPlaybookLedger.QueryEntriesAsync</c>'s own convention); the
    /// chain-verification summary still covers the owner's full chain — verifying tamper-evidence
    /// of the whole chain discloses no entry content, unlike the bundle's own entries/events.
    /// </param>
    Task<PlaybookEvidenceExport> ExportAsync(
        string ownerId,
        string exportedBy,
        CancellationToken cancellationToken = default,
        IReadOnlySet<Guid>? allowedNamespaceIds = null);
}
