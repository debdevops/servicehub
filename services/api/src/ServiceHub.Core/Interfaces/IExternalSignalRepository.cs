using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store for <see cref="ExternalSignalEvent"/> (M5 of the persistence wave, ADR-0008) —
/// C3's raw input. No hash chain, no FK, owner-partitioned like every other table in this
/// codebase. See <see cref="ExternalSignalEvent"/> for why this is not part of either ledger.
/// </summary>
public interface IExternalSignalRepository
{
    /// <summary>Records one external signal. <see cref="RecordExternalSignalRequest.Source"/> is
    /// mandatory; a blank value fails validation.</summary>
    Task<Result<ExternalSignalEvent>> RecordAsync(
        RecordExternalSignalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries signals for one owner, optionally filtered by namespace, most recent first.
    /// <paramref name="namespaceId"/> filters to signals scoped to exactly that namespace — it
    /// does not implicitly include fleet-wide (<c>NamespaceId == null</c>) signals; a caller
    /// wanting both must query separately or omit the filter, mirroring
    /// <c>IRecoveryLedger.QueryEntriesAsync</c>'s own namespace-filter convention.
    /// </summary>
    Task<IReadOnlyList<ExternalSignalEvent>> QueryAsync(
        string ownerId,
        Guid? namespaceId,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default);
}
