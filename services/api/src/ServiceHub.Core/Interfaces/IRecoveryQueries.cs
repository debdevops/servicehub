using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>What the ledger says, read-only: the summary, the list and one entry opened (units 2.12–2.13).</summary>
public interface IRecoveryQueries
{
    /// <summary>The summary over the scope. See <see cref="RecoverySummary"/>.</summary>
    Task<RecoverySummary> SummariseAsync(RecoveryScope scope, string window, CancellationToken cancellationToken);

    /// <summary>Ledger entries in scope, newest first, optionally only one state.</summary>
    Task<RecoveryEntryPage> ListAsync(RecoveryScope scope, string window, RecoveryEntryState? state, int page, int pageSize, CancellationToken cancellationToken, RecoveryListFilter? filter = null);

    /// <summary>
    /// Names failure signatures in words — "MaxDeliveryCountExceeded on orders (Azure)" — from a dead letter that carries each hash.
    /// A hash no recorded dead letter carries any more is simply left out; the caller says "a failure" rather than inventing a name.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> DescribeSignaturesAsync(string ownerId, IReadOnlyCollection<string> hashes, CancellationToken cancellationToken);

    /// <summary>One entry and its events, or null when it is not in scope.</summary>
    Task<RecoveryEntryDetail?> GetAsync(RecoveryScope scope, Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// The newest ledger events one actor recorded for one owner (unit 4.4 — an agent's timeline). An actor ending in ':'
    /// is a prefix (an agent that records per-rule identities); otherwise it must match exactly.
    /// </summary>
    Task<IReadOnlyList<Entities.RecoveryEvent>> EventsByActorAsync(string ownerId, string actor, int limit, CancellationToken cancellationToken);

    /// <summary>Every ledger event of one owner, in chain (Seq) order — the evidence export (unit 6.12).</summary>
    Task<IReadOnlyList<Entities.RecoveryEvent>> ChainAsync(string ownerId, CancellationToken cancellationToken);
}

/// <summary>
/// Who is asking and what they may see: their owner, the namespace allow-list of their credential (null =
/// unrestricted), and the filters they chose. The same scoping as every other query.
/// </summary>
public sealed record RecoveryScope(
    string OwnerId,
    IReadOnlySet<Guid>? AllowedNamespaceIds,
    Guid? NamespaceId = null,
    CloudProviderType? Provider = null,
    EnvironmentType? Environment = null);
