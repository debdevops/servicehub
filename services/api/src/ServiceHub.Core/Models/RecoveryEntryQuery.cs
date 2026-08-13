using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Filter for <c>IRecoveryLedger.QueryEntriesAsync</c>. All filters beyond
/// <see cref="OwnerId"/> are optional and combine with AND.</summary>
public sealed class RecoveryEntryQuery
{
    /// <summary>Owner ID — always required; every query is owner-scoped.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Restrict to entries under a specific operation.</summary>
    public Guid? OperationId { get; init; }

    /// <summary>Restrict to entries in a specific namespace.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Restrict to entries currently in one of these states. Null means no restriction.</summary>
    public IReadOnlyCollection<RecoveryEntryState>? States { get; init; }

    /// <summary>Maximum number of entries to return, most recently begun first.</summary>
    public int Limit { get; init; } = 100;
}
