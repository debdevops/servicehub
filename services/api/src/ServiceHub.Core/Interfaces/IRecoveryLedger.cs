using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The sole writer and reader of the Recovery Evidence Ledger — a durable, attributable,
/// owner-isolated, hash-chained record of every recovery decision ServiceHub makes and its
/// eventual outcome. See <see cref="RecoveryOperation"/>, <see cref="RecoveryLedgerEntry"/>,
/// and <see cref="RecoveryEvent"/>.
/// </summary>
/// <remarks>
/// No method here accepts a caller-supplied actor identity string — every actor enters as a
/// <see cref="Models.RecoveryActor"/>, resolved server-side (see <c>ActorIdentityResolver</c>).
/// State transitions are enforced internally; illegal transitions and owner mismatches return a
/// <see cref="Result{T}"/> failure, never an exception. Wired into every provider-mutating path —
/// <c>RecoveryController</c>, <c>MessagesController</c>, <c>RulesController</c>,
/// <c>AutoReplayExecutor</c>, <c>BulkOperationExecutor</c>, <c>SignatureReplayExecutor</c> — plus
/// the background workers that observe and age entries.
/// </remarks>
public interface IRecoveryLedger
{
    /// <summary>Opens a new <see cref="RecoveryOperation"/> — the immutable header for one
    /// operator/automation decision.</summary>
    Task<Result<RecoveryOperation>> OpenOperationAsync(
        OpenRecoveryOperationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Begins a new <see cref="RecoveryLedgerEntry"/> under an open operation, in
    /// state <see cref="Enums.RecoveryEntryState.Executing"/>.</summary>
    Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(
        BeginRecoveryEntryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records a provider call's outcome against an entry that must currently be
    /// <see cref="Enums.RecoveryEntryState.Executing"/>. Transitions to
    /// <see cref="Enums.RecoveryEntryState.Observing"/> (replay, accepted),
    /// <see cref="Enums.RecoveryEntryState.Discarded"/> (purge, accepted),
    /// <see cref="Enums.RecoveryEntryState.ExecutionFailed"/> (rejected), or
    /// <see cref="Enums.RecoveryEntryState.ExecutionUnknown"/> (outcome unknown).</summary>
    Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(
        RecordExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records a DLQ-scan observation against an entry that must currently be
    /// <see cref="Enums.RecoveryEntryState.Observing"/>. Transitions to
    /// <see cref="Enums.RecoveryEntryState.Returned"/>, <see cref="Enums.RecoveryEntryState.Recovered"/>,
    /// or <see cref="Enums.RecoveryEntryState.Unverified"/>.</summary>
    Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(
        RecordObservationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Operator declaration that a non-terminal entry is unrecoverable. Transitions to
    /// <see cref="Enums.RecoveryEntryState.WrittenOff"/>. Fails if the entry is already terminal,
    /// or <paramref name="reason"/> is empty.</summary>
    Task<Result<RecoveryLedgerEntry>> SetDispositionAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a free-text operator annotation to an entry. Does not change entry
    /// state and is legal regardless of the entry's current state — it is additional evidence,
    /// not a transition.</summary>
    Task<Result<RecoveryEvent>> AppendNoteAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        string note,
        CancellationToken cancellationToken = default);

    /// <summary>Recomputes and compares one owner's hash chain, returning the first divergent
    /// <see cref="RecoveryEvent.Seq"/> if any is found. Tamper-EVIDENT, not tamper-PROOF.</summary>
    Task<ChainVerificationResult> VerifyChainAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one operation by ID, scoped to its owner. Returns null if it doesn't exist
    /// or belongs to a different owner.</summary>
    Task<RecoveryOperation?> GetOperationAsync(
        Guid operationId,
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>Queries operations for one owner, optionally filtered by namespace, most
    /// recently opened first.</summary>
    Task<IReadOnlyList<RecoveryOperation>> QueryOperationsAsync(
        string ownerId,
        Guid? namespaceId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Queries ledger entries for one owner, optionally filtered by operation,
    /// namespace, and/or state.</summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> QueryEntriesAsync(
        RecoveryEntryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an owner's currently non-terminal entries, oldest first. No ageing
    /// threshold or flagging logic is applied yet — that is a later phase's ageing worker.</summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the <see cref="Enums.RecoveryEntryState.Observing"/> entry carrying this exact
    /// <c>x-servicehub-recovery-id</c> marker, if any — the exact-match arm of recurrence
    /// detection (§8.5). Backed by the unique-where-not-null index on
    /// <see cref="RecoveryLedgerEntry.RecoveryMarker"/>, so at most one entry can ever match.
    /// </summary>
    Task<RecoveryLedgerEntry?> FindByMarkerAsync(
        string ownerId,
        string marker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every <see cref="Enums.RecoveryEntryState.Observing"/> entry in one entity that
    /// could not have its marker applied (<see cref="RecoveryLedgerEntry.MarkerApplied"/> is
    /// false) and shares the given body hash — the heuristic-fallback candidate set for
    /// recurrence detection (§8.3). More than one result means the match is ambiguous; the
    /// caller records that collision count rather than picking one candidate arbitrarily.
    /// </summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> FindHeuristicRecurrenceCandidatesAsync(
        string ownerId,
        Guid? namespaceId,
        string entityName,
        string bodyHash,
        DateTimeOffset beganBefore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every event belonging to one operation (across all its entries plus the
    /// operation-level <see cref="Enums.RecoveryEventType.OperationOpened"/> event),
    /// <see cref="RecoveryEvent.Seq"/>-ordered — the evidence export's <c>events.json</c> source
    /// and the operation detail page's event-chain view.
    /// </summary>
    Task<IReadOnlyList<RecoveryEvent>> GetEventsForOperationAsync(
        Guid operationId,
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an <see cref="Enums.RecoveryEventType.AgeingFlagged"/> event already exists for
    /// this entry — lets the ageing worker decide whether its next call should be
    /// <see cref="FlagAgeingAsync"/> or <see cref="ExpireEntryAsync"/>.
    /// </summary>
    Task<bool> HasAgeingFlagAsync(
        Guid entryId,
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently flags a non-terminal entry as having passed the ageing threshold — the
    /// event <see cref="Enums.RecoveryEntryState.Expired"/> must be preceded by (roadmap §7.2).
    /// Does not change <see cref="RecoveryLedgerEntry.State"/>. A no-op (returns the entry
    /// unchanged, no duplicate event) if the entry was already flagged or is already terminal, so
    /// a restarted or overlapping ageing sweep cannot double-flag it.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> FlagAgeingAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        int ageInDays,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an already-flagged, still-non-terminal entry to
    /// <see cref="Enums.RecoveryEntryState.Expired"/>. Fails if the entry is already terminal, or
    /// if its most recent event is not <see cref="Enums.RecoveryEventType.AgeingFlagged"/> —
    /// structurally enforcing that <c>Expired</c> is reachable only through a transition whose
    /// preceding event flagged it first.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> ExpireEntryAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every <see cref="RecoveryLedgerEntry"/> sharing the recurrence-lineage key
    /// (<paramref name="ownerId"/>, <paramref name="namespaceId"/>, <paramref name="entityName"/>,
    /// <paramref name="bodyHash"/>) begun on or after <paramref name="since"/> — the read-only
    /// lookback roadmap §7.5 defines for the auto-replay recurrence-cap check. Regardless of
    /// entry state: each matched row represents one past recovery attempt against this lineage.
    /// </summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> FindLineageMatchesAsync(
        string ownerId,
        Guid? namespaceId,
        string entityName,
        string bodyHash,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a new <see cref="RecoveryLedgerEntry"/> already in terminal
    /// <see cref="Enums.RecoveryEntryState.Declined"/> state, plus its
    /// <see cref="Enums.RecoveryEventType.EligibilityDeclined"/> event, atomically in one
    /// <c>SaveChanges</c> — the roadmap §9.3 mechanism for durably recording a safety-gate
    /// decision in the Recovery Evidence Ledger instead of <c>AuditService</c>. Unlike
    /// <see cref="BeginEntryAsync"/>, this never implies a provider call: no
    /// <see cref="Enums.RecoveryEntryState.Executing"/> phase exists for a declined attempt.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> RecordDeclinedAsync(
        BeginRecoveryEntryRequest request,
        string reasonCode,
        string? detailJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Database-side aggregation of terminal <see cref="Enums.RecoveryDisposition"/> counts for
    /// one signature's evidence population under one <see cref="Enums.RecoveryOperationKind"/> —
    /// the source query for Evidence-Derived Trust Scoring (roadmap §8.10). Joined to the parent
    /// <see cref="RecoveryOperation.Kind"/> so a rejected purge attempt sharing the same
    /// <paramref name="signatureHash"/> can never be counted toward a replay trust score (both
    /// can independently produce <see cref="Enums.RecoveryDisposition.Failed"/>). Entries with a
    /// null <see cref="RecoveryLedgerEntry.Disposition"/> (non-terminal) are excluded.
    /// </summary>
    Task<IReadOnlyDictionary<RecoveryDisposition, int>> GetDispositionCountsAsync(
        string ownerId,
        string signatureHash,
        RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every distinct, non-null <see cref="RecoveryLedgerEntry.SignatureHashSnapshot"/> value
    /// with at least one entry under one <see cref="Enums.RecoveryOperationKind"/>, scoped to
    /// <paramref name="ownerId"/> — the sweep set for <c>AutonomyEvaluationWorker</c>. A
    /// null <c>SignatureHashSnapshot</c> is never returned: it has no per-signature trust
    /// identity to evaluate (roadmap §4 — <c>BodyHash</c> lineage and <c>SignatureHash</c> trust
    /// semantics are kept separate).
    /// </summary>
    Task<IReadOnlyList<string>> GetDistinctSignatureHashesAsync(
        string ownerId,
        RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an <see cref="Entities.AutonomyGrant"/> promotion or demotion — updates (or, on
    /// first promotion, inserts) the grant's current-level projection and appends the paired,
    /// immutable <see cref="Enums.RecoveryEventType.AutonomyGrantPromoted"/>/
    /// <see cref="Enums.RecoveryEventType.AutonomyGrantDemoted"/> forensic event, atomically in
    /// one <c>SaveChanges</c> (roadmap §9.4.3). Fails validation if <paramref name="reason"/> is
    /// empty or <paramref name="previousLevel"/> equals <paramref name="newLevel"/> (not a real
    /// transition). A losing concurrent write — either a first-creation uniqueness race or a
    /// mutation-concurrency-token race (roadmap §9.4.4) — throws
    /// <c>DbUpdateException</c>/<c>DbUpdateConcurrencyException</c> rather than
    /// returning a <see cref="Result{T}"/> failure, matching this codebase's existing convention
    /// for genuine concurrent-write races (see <c>DlqMessage.Status</c>): the caller is expected
    /// to catch it and simply re-evaluate on its next sweep, never treating it as a caller error.
    /// </summary>
    Task<Result<AutonomyGrant>> RecordAutonomyGrantTransitionAsync(
        string ownerId,
        string signatureHash,
        RecoveryOperationKind actionKind,
        AutonomyLevel previousLevel,
        AutonomyLevel newLevel,
        string reason,
        string? evidenceJson,
        CancellationToken cancellationToken = default);
}
