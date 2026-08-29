using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One hash-chained fact about a <see cref="PlaybookEntry"/>'s lifecycle — the Playbook Ledger's
/// evidence itself, structurally identical in spirit to <see cref="RecoveryEvent"/> but on a fully
/// independent chain: no shared <see cref="Seq"/> space, no cross-chain interleaving, no FK to or
/// from any Recovery table. An owner's Playbook chain and Recovery chain never interleave, and
/// tampering with one is undetectable by verifying the other — correct, because they are two
/// different ledgers, not two views of one.
/// <para>
/// Fully <c>init</c>-only — append-only, enforced by <c>PlaybookLedgerAppendOnlyGuard</c>.
/// </para>
/// </summary>
public sealed class PlaybookEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID — the hash-chain partition key, matching <see cref="PlaybookEntry.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Monotonic sequence number, per owner, gapless from 1 — the Playbook chain's own
    /// independent sequence space (never shared with <see cref="RecoveryEvent.Seq"/>).</summary>
    public required long Seq { get; init; }

    /// <summary>The entry this event belongs to. Plain scalar Guid column, not a navigation FK.</summary>
    public required Guid EntryId { get; init; }

    /// <summary>What happened.</summary>
    public required PlaybookEventType EventType { get; init; }

    /// <summary>When it happened.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The resolved actor identity — re-stated per event, never inherited from the entry,
    /// and never caller-supplied.</summary>
    public required string ActorIdentity { get; init; }

    /// <summary>The resolved actor kind — re-stated per event, never inherited from the entry.</summary>
    public required PlaybookActorKind ActorKind { get; init; }

    /// <summary>Free-form detail, redacted via <c>LogRedactor.Redact</c> before persisting — e.g. a
    /// rejection reason. Never a message body or credential.</summary>
    public string? DetailJson { get; init; }

    /// <summary>The previous event's <see cref="EntryHash"/> in this owner's chain, or 64 zeros
    /// (<c>PlaybookHashChain.GenesisHash</c>) for the first event.</summary>
    public required string PrevHash { get; init; }

    /// <summary>SHA-256 hash of this event's canonical fields plus <see cref="PrevHash"/>.</summary>
    public required string EntryHash { get; init; }

    /// <summary>Schema version of this event's canonical hash-input shape — lets the hash
    /// algorithm evolve without invalidating already-written history.</summary>
    public required int SchemaVersion { get; init; }
}
