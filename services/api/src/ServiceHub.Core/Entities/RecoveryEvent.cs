using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One append-only, hash-chained fact in the Recovery Evidence Ledger — the evidence itself.
/// Every property is <c>init</c>-only; nothing on this entity is ever mutable, and the
/// append-only guard in <c>DlqDbContext.SaveChangesAsync</c> rejects any <c>Modified</c> or
/// <c>Deleted</c> change against it.
/// </summary>
/// <remarks>
/// Chained per <see cref="OwnerId"/> (not globally, not per-operation): <see cref="EntryHash"/>
/// is a SHA-256 of this event's fields (excluding itself) concatenated with <see cref="PrevHash"/>,
/// and <see cref="Seq"/> is monotonically increasing per owner. This is tamper-EVIDENT, not
/// tamper-PROOF — anyone with write access to the SQLite file can recompute the whole chain; it
/// detects casual or partial alteration, nothing more.
/// </remarks>
public sealed class RecoveryEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID — the hash-chain partition key. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Monotonically increasing per <see cref="OwnerId"/>. Unique on (OwnerId, Seq).</summary>
    public required long Seq { get; init; }

    /// <summary>The <see cref="RecoveryLedgerEntry"/> this event concerns. Null for
    /// operation-level events (currently only <see cref="RecoveryEventType.OperationOpened"/>).</summary>
    public Guid? EntryId { get; init; }

    /// <summary>The <see cref="RecoveryOperation"/> this event belongs to.</summary>
    public required Guid OperationId { get; init; }

    /// <summary>The kind of fact this event records.</summary>
    public required RecoveryEventType EventType { get; init; }

    /// <summary>When this event occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Server-derived actor identity, re-stated per event — never inherited from the operation
    /// and never a caller-supplied string. See <see cref="Models.RecoveryActor"/>.
    /// </summary>
    public required string ActorIdentity { get; init; }

    /// <summary>The category of actor behind this event.</summary>
    public required RecoveryActorKind ActorKind { get; init; }

    /// <summary>
    /// Structured, redacted detail (via <c>LogRedactor</c>) for this event. Never contains a
    /// message body, body preview, connection string, or credential.
    /// </summary>
    public string? DetailJson { get; init; }

    /// <summary>The previous event's <see cref="EntryHash"/> for this <see cref="OwnerId"/>.
    /// 64 zeros for the first event in an owner's chain.</summary>
    public required string PrevHash { get; init; }

    /// <summary>SHA-256 hex digest of this event's fields (excluding this one) concatenated
    /// with <see cref="PrevHash"/>.</summary>
    public required string EntryHash { get; init; }

    /// <summary>Schema version of this event's shape. Starts at 1.</summary>
    public required int SchemaVersion { get; init; }
}
