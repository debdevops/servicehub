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
    /// <param name="ownerId">Tenant-isolation filter — only this owner's signals are returned.</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="start">Start of the query window.</param>
    /// <param name="end">End of the query window.</param>
    /// <param name="limit">Maximum number of signals to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="allowedNamespaceIds">
    /// The caller's credential's namespace allow-list, when one is present — null means
    /// unrestricted. Applies even when <paramref name="namespaceId"/> is omitted, so a restricted
    /// caller can't read every namespace's signals by simply not filtering; a fleet-wide
    /// (<c>NamespaceId == null</c>) signal is excluded when restricted, same reasoning as
    /// <c>IPlaybookLedger.QueryEntriesAsync</c>'s own allow-list.
    /// </param>
    Task<IReadOnlyList<ExternalSignalEvent>> QueryAsync(
        string ownerId,
        Guid? namespaceId,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default,
        IReadOnlySet<Guid>? allowedNamespaceIds = null);
}
