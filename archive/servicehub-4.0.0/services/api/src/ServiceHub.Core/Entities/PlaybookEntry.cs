using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One proposal's full lifecycle (M4 of the persistence wave) — the Playbook Ledger's answer to
/// "what did ServiceHub, or a reasoning layer over it, believe was worth doing, on what grounds,
/// and what did a human decide about that belief." Structurally distinct from the Recovery
/// Evidence Ledger (<see cref="RecoveryLedgerEntry"/>), which answers "what did ServiceHub do, and
/// how do we know it worked" — this ledger is for reasoning, never for execution. Nothing written
/// here ever authorizes a replay or purge.
/// <para>
/// Immutable identity/context block set once at <c>ProposeAsync</c>: everything except the mutable
/// projection below (<see cref="State"/>, <see cref="Disposition"/>, <see cref="LastEventSeq"/>,
/// <see cref="ClosedAt"/>), enforced by <c>PlaybookLedgerAppendOnlyGuard</c> — the same split
/// <see cref="RecoveryLedgerEntry"/> already uses. The mutable projection is itself derived from,
/// and never the source of truth ahead of, the <see cref="PlaybookEvent"/> chain for this entry.
/// </para>
/// </summary>
public sealed class PlaybookEntry
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID — the hash-chain partition key, never global, never per-namespace.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Which of the four autonomy pillars this proposal belongs to.</summary>
    public required PillarKind PillarKind { get; init; }

    /// <summary>
    /// A bounded, code-validated string (e.g. <c>"AnomalyFlag"</c>, <c>"DriftFinding"</c>,
    /// <c>"CorrelationHypothesis"</c>, <c>"ReplayPlan"</c>, <c>"NarrationDraft"</c>) — deliberately
    /// not an enum: proposal shapes will keep growing per-pillar for years, and an enum column
    /// would force a schema migration every time one ships. Each writer validates against its own
    /// small known set before insert; the ledger itself treats this as opaque.
    /// </summary>
    public required string ProposalKind { get; init; }

    /// <summary>
    /// What grounded this proposal — by reference/hash, never inline raw data (never a message
    /// body or preview). Passed through <c>LogRedactor.Redact</c> before persisting.
    /// </summary>
    public required string EvidenceRefJson { get; init; }

    /// <summary>
    /// What is being suggested — structured JSON, one shape per <see cref="ProposalKind"/>, opaque
    /// to the ledger itself. Passed through <c>LogRedactor.Redact</c> before persisting. Never
    /// mutated in place after creation — a revision writes a new <see cref="PlaybookEvent"/> with
    /// the new proposal content, so "what was actually proposed" stays reconstructable.
    /// </summary>
    public required string ProposalJson { get; init; }

    /// <summary>When this entry was created.</summary>
    public required DateTimeOffset ProposedAt { get; init; }

    /// <summary>The resolved actor identity that created this proposal — never caller-supplied.</summary>
    public required string ProposerIdentity { get; init; }

    /// <summary>Whether a deterministic worker, a human, or (once it exists) the reasoning
    /// companion created this proposal — permanently distinguishable, by design.</summary>
    public required PlaybookActorKind ProposerKind { get; init; }

    /// <summary>Soft reference to the same signature identity Recovery already uses (<c>FailureSignature</c>/
    /// <c>SignatureLifecycleState</c> hash) — no FK. Null when this proposal isn't signature-scoped.</summary>
    public string? SignatureHashSnapshot { get; init; }

    /// <summary>Soft reference to the namespace this proposal concerns — no FK, same
    /// nullable-narrowing pattern as <see cref="RecoveryLedgerEntry.NamespaceId"/>. Null means
    /// fleet-wide (e.g. a cross-namespace correlation hypothesis).</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Snapshot of the namespace's name at proposal time — survives namespace deletion.</summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>Snapshot of the namespace's cloud provider at proposal time.</summary>
    public Enums.CloudProviderType? ProviderSnapshot { get; init; }

    /// <summary>Snapshot of the namespace's environment at proposal time.</summary>
    public Enums.EnvironmentType? EnvironmentSnapshot { get; init; }

    /// <summary>
    /// Recover-pillar only: soft reference (no FK) to the <see cref="RecoveryOperation"/> a human
    /// executed off the back of this proposal, if any — read-only cross-reference for the UI,
    /// never a join ServiceHub relies on for correctness. Approving a replay-plan proposal here
    /// means "a human agrees this plan is sound"; it never itself calls
    /// <c>IRecoveryLedger.OpenOperationAsync</c>.
    /// </summary>
    public Guid? RelatedRecoveryOperationId { get; init; }

    /// <summary>When this entry expires without a human decision (see <see cref="PlaybookEntryState.Expired"/>).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    // ── Mutable projection — derived from the PlaybookEvent chain, writable only through
    // IPlaybookLedger, never mutated directly. Enforced by PlaybookLedgerAppendOnlyGuard.

    /// <summary>Current lifecycle state.</summary>
    public PlaybookEntryState State { get; set; } = PlaybookEntryState.Proposed;

    /// <summary>The terminal human decision, once one has been made. Null while non-terminal, and
    /// while terminal for a reason other than a human decision (<c>Expired</c>/<c>Superseded</c>).</summary>
    public PlaybookDisposition? Disposition { get; set; }

    /// <summary>The <see cref="PlaybookEvent.Seq"/> of the most recent event for this entry.</summary>
    public long LastEventSeq { get; set; }

    /// <summary>When this entry reached a terminal state. Null while still open.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
}
