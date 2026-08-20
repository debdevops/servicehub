using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Builds a Recovery Evidence Ledger export package for one operation — the manifest (§16.3),
/// the operation header, its entries, and its full hash-chained event log — from durable ledger
/// state only. See <see cref="RecoveryEvidenceExport"/> for the reproducibility guarantee.
/// </summary>
public interface IRecoveryEvidenceExporter
{
    /// <summary>
    /// Builds the export for one operation, scoped to its owner. Fails with
    /// <see cref="ErrorType.NotFound"/> if the operation doesn't exist or belongs to a different
    /// owner.
    /// </summary>
    /// <param name="operationId">The operation to export.</param>
    /// <param name="ownerId">The caller's owner ID — every read is owner-scoped.</param>
    /// <param name="exportedBy">Server-derived identity of the caller requesting the export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<RecoveryEvidenceExport>> ExportAsync(
        Guid operationId,
        string ownerId,
        string exportedBy,
        CancellationToken cancellationToken = default);
}
