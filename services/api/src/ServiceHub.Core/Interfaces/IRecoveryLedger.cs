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

    /// <summary>
    /// Records that predicate 3's recurrence-lineage cap (roadmap §7.5) was reached for this
    /// entry's lineage but a <c>User</c>/<c>ApiKey</c> actor was not auto-denied (§29.11 Option
    /// B, §9.4.1) — appends a <see cref="Enums.RecoveryEventType.RecurrenceCapObserved"/> event
    /// carrying <paramref name="reasonCode"/>/<paramref name="matchedCount"/> as structured
    /// <c>DetailJson</c>. A direct sibling of <see cref="AppendNoteAsync"/>: does not change
    /// entry state and is legal regardless of the entry's current state — additional
    /// observability evidence, never a state transition, never implying the entry itself is
    /// anomalous.
    /// </summary>
    Task<Result<RecoveryEvent>> RecordRecurrenceContextAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        string reasonCode,
        int matchedCount,
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

    /// <summary>Returns an owner's currently non-terminal entries, oldest first, capped at
    /// <paramref name="limit"/>. No ageing threshold or flagging logic is applied here — that is
    /// the ageing/verification workers' job. Oldest-first ordering makes a cap safe across
    /// sweeps: a backlog beyond <paramref name="limit"/> is picked up on a later sweep as the
    /// oldest entries ahead of it terminalize, never starved.</summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(
        string ownerId,
        int limit = int.MaxValue,
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
    /// <paramref name="ownerId"/>, capped at <paramref name="limit"/> — the sweep set for
    /// <c>AutonomyEvaluationWorker</c>. A null <c>SignatureHashSnapshot</c> is never returned: it
    /// has no per-signature trust identity to evaluate (roadmap §4 — <c>BodyHash</c> lineage and
    /// <c>SignatureHash</c> trust semantics are kept separate).
    /// </summary>
    /// <remarks>
    /// Known limitation, honestly documented rather than silently accepted: unlike
    /// <see cref="GetAgeingAsync"/>, this has no natural "oldest first" ordering to make a cap
    /// self-draining across sweeps, so results are ordered by the hash string itself for
    /// determinism only. If an owner's distinct-signature count persistently exceeds
    /// <paramref name="limit"/>, the signatures sorting after the cap are not guaranteed
    /// round-robin fairness across sweeps without a schema change (e.g. a last-evaluated
    /// timestamp) this increment deliberately does not add.
    /// </remarks>
    Task<IReadOnlyList<string>> GetDistinctSignatureHashesAsync(
        string ownerId,
        RecoveryOperationKind actionKind,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The <see cref="RecoveryLedgerEntry.ProviderSnapshot"/> shared by every entry recorded
    /// against <paramref name="signatureHash"/> for <paramref name="ownerId"/> — a signature is
    /// provider-specific by construction (<c>FailureFingerprintBuilder</c> hashes the message's
    /// provider into the canonical string), so one non-null snapshot is authoritative for the
    /// whole signature. <see langword="null"/> if no entry exists yet. Roadmap §14's
    /// <c>CanProveDlqAbsence</c>-equivalent L4/L5 promotion guard (Phase F).
    /// </summary>
    Task<CloudProviderType?> GetSignatureProviderAsync(
        string ownerId,
        string signatureHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent <paramref name="count"/> terminal outcomes carrying real verification
    /// signal for one signature — <see cref="Enums.RecoveryDisposition.Recovered"/> or
    /// <see cref="Enums.RecoveryDisposition.Returned"/> only — ordered by
    /// <see cref="RecoveryLedgerEntry.LastEventSeq"/> descending, the per-owner monotonic
    /// hash-chain sequence, not wall-clock time (roadmap §7.6/§8.5/§8.6's "two consecutive
    /// verified `Returned` outcomes" fast-demotion source query). <see cref="Enums.RecoveryDisposition.Unverified"/>
    /// entries are excluded entirely rather than treated as a streak-breaking value — the same
    /// exclusion §8.10 applies to <c>verified_success_rate</c>, since a provider's inability to
    /// prove absence is not evidence against the signature (§14). <see cref="Enums.RecoveryDisposition.Failed"/>
    /// can never appear here: it is only ever set by <see cref="RecordExecutionAsync"/>'s
    /// rejection branch, before any observation window opens, never by
    /// <see cref="RecordObservationAsync"/> — this query only reads verification-window outcomes.
    /// </summary>
    Task<IReadOnlyList<RecoveryDisposition>> GetRecentVerifiedDispositionsAsync(
        string ownerId,
        string signatureHash,
        RecoveryOperationKind actionKind,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same query as <see cref="GetRecentVerifiedDispositionsAsync"/>, but scoped to one
    /// <see cref="Entities.AutoReplayRule"/> (via <see cref="RecoveryOperation.SourceRuleId"/>)
    /// rather than one failure signature — the success-rate circuit breaker's source query. A
    /// rule fires against many different signatures, so this cannot reuse the per-signature
    /// query's key.
    /// </summary>
    Task<IReadOnlyList<RecoveryDisposition>> GetRecentVerifiedDispositionsByRuleAsync(
        string ownerId,
        long ruleId,
        RecoveryOperationKind actionKind,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the success-rate circuit breaker automatically disabling an
    /// <see cref="Entities.AutoReplayRule"/> — opens a minimal <see cref="RecoveryOperation"/>
    /// (<c>Kind = AutoReplayRuleControl</c>) and appends one
    /// <see cref="Enums.RecoveryEventType.AutoReplayRuleCircuitBreakerTripped"/> event at the
    /// operation level, atomically, mirroring <see cref="RecordEmergencyControlEventAsync"/>'s
    /// pattern. Does not itself flip <see cref="Entities.AutoReplayRule.Enabled"/> — the caller
    /// is expected to have already done so in the same save, or immediately after.
    /// </summary>
    Task<Result<RecoveryOperation>> RecordAutoReplayCircuitBreakerTripAsync(
        string ownerId,
        long ruleId,
        string ruleName,
        RecoveryActor actor,
        int sampleSize,
        double verifiedSuccessRate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an <see cref="Entities.AutonomyGrant"/> promotion or demotion — updates (or, on
    /// first promotion, inserts) the grant's current-level projection and appends the paired,
    /// immutable <see cref="Enums.RecoveryEventType.AutonomyGrantPromoted"/>/
    /// <see cref="Enums.RecoveryEventType.AutonomyGrantDemoted"/> forensic event, atomically in
    /// one <c>SaveChanges</c> (roadmap §9.4.3). Fails validation if <paramref name="reason"/> is
    /// empty, <paramref name="previousLevel"/> equals <paramref name="newLevel"/> (not a real
    /// transition), or an existing grant's actual <see cref="Entities.AutonomyGrant.CurrentLevel"/>
    /// no longer matches <paramref name="previousLevel"/> — the second case guards against two
    /// independent callers (e.g. the hourly sweep and an event-time fast-demotion check) both
    /// deciding to write the same transition from a snapshot read before either one wrote: without
    /// this check the second write would silently re-apply, logging a duplicate forensic event
    /// with a stale <paramref name="previousLevel"/>. A losing concurrent write — either a first-creation uniqueness race or a
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

    /// <summary>
    /// Reads the current <see cref="Entities.AutonomyGrant"/> projection for one
    /// <c>(OwnerId, SignatureHash, ActionKind)</c> triple, or <see langword="null"/> if none has
    /// ever been created — a signature with no row has never been promoted past L3, the floor
    /// every signature can always stand on (roadmap §8.4). Read by both the Eligibility Gate's
    /// predicate 5 (§9.4.3, enforced) and <c>AutonomyEvaluationWorker</c>'s own read of current
    /// standing before deciding a promotion/demotion.
    /// </summary>
    Task<AutonomyGrant?> GetAutonomyGrantAsync(
        string ownerId,
        string signatureHash,
        RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Live, owner-scoped emergency-stop read for the Eligibility Gate's predicate 0 (roadmap
    /// §9.4.2, §15.2) — <see langword="true"/> iff the most recent event for
    /// <paramref name="ownerId"/> whose <see cref="Enums.RecoveryEventType"/> is
    /// <see cref="Enums.RecoveryEventType.EmergencyStopActivated"/> or
    /// <see cref="Enums.RecoveryEventType.EmergencyStopCleared"/> (ordered by
    /// <see cref="RecoveryEvent.Seq"/> descending) is an <c>Activated</c> event.
    /// <see langword="false"/> if none exists or the most recent is a <c>Cleared</c> event. No
    /// stored flag and no second table — this is a live query over the same
    /// <see cref="RecoveryEvent"/> table everything else already uses.
    /// </summary>
    Task<bool> IsEmergencyStopActiveAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an owner-scoped emergency-stop activation or clearing (roadmap §9.4.2, §15.2) —
    /// opens a minimal <see cref="RecoveryOperation"/> (<c>Kind = EmergencyControl</c>,
    /// <c>Trigger = EmergencyControl</c>, <c>TargetCount = 0</c>) and appends one
    /// <see cref="Enums.RecoveryEventType.EmergencyStopActivated"/>/
    /// <see cref="Enums.RecoveryEventType.EmergencyStopCleared"/> event at the operation level
    /// (<c>EntryId = null</c>), atomically. Always appends a fresh event even if the owner's
    /// state already matches <paramref name="activate"/> — re-activating during an unresolved
    /// incident is itself a meaningful, separately timestamped fact, not noise. Never reads,
    /// writes, or otherwise touches an <see cref="AutonomyGrant"/> row: clearing restores the
    /// *ability* of a previously valid grant to execute again, it never resets or re-grants
    /// anything. Callers must resolve <paramref name="actor"/> through an HTTP-only path (never
    /// a caller-supplied kind) so this can never be reached by an <c>Automation</c>/<c>System</c>
    /// actor — see <c>RecoveryController</c>'s <c>ResolveRecoveryActor()</c> and the
    /// <c>admin</c>-scope requirement on its emergency-stop endpoints.
    /// </summary>
    Task<Result<RecoveryOperation>> RecordEmergencyControlEventAsync(
        string ownerId,
        RecoveryActor actor,
        bool activate,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a <see cref="Enums.RecoveryOutcomeFlagKind"/> operator attestation to an entry
    /// (roadmap §8.10, §9.3) — appends a <see cref="Enums.RecoveryEventType.OutcomeFlagged"/>
    /// event carrying <paramref name="flagKind"/>/<paramref name="reason"/> as structured
    /// <c>DetailJson</c>. A direct sibling of <see cref="AppendNoteAsync"/>/
    /// <see cref="RecordRecurrenceContextAsync"/>: does not change entry state and is legal
    /// regardless of the entry's current state, including terminal entries — the disqualifying
    /// judgment this records can be reached after an entry has already closed. Callers must
    /// resolve <paramref name="actor"/> through an HTTP-only path so this can never be reached by
    /// an <c>Automation</c>/<c>System</c> actor (roadmap: "never system- or AI-inferred").
    /// </summary>
    Task<Result<RecoveryEvent>> RecordOutcomeFlagAsync(
        Guid entryId,
        string ownerId,
        RecoveryActor actor,
        RecoveryOutcomeFlagKind flagKind,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// §8.10's <c>unsafe_outcome_count &gt; 0</c> read — <see langword="true"/> iff any entry for
    /// <paramref name="ownerId"/>, regardless of signature, carries an
    /// <see cref="Enums.RecoveryEventType.OutcomeFlagged"/> event with
    /// <see cref="Enums.RecoveryOutcomeFlagKind.Unsafe"/>. Deliberately fleet-level, not scoped
    /// to one signature (roadmap §8.10: "Fleet-level, not per-signature, for the flag's
    /// disqualifying effect") — unlike <see cref="HasDuplicateAssociationAsync"/>, a confirmed
    /// unsafe outcome anywhere in an owner's fleet withholds L4/L5 for every signature under that
    /// owner, not only the flagged one.
    /// </summary>
    Task<bool> HasUnsafeOutcomeFlagAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// §8.10's <c>duplicate_association</c> read — <see langword="true"/> iff any entry for the
    /// exact <c>(OwnerId, SignatureHash)</c> pair carries an
    /// <see cref="Enums.RecoveryEventType.OutcomeFlagged"/> event with
    /// <see cref="Enums.RecoveryOutcomeFlagKind.DuplicateBusinessEffect"/>. Per-signature, not
    /// fleet-level (roadmap §8.10) — a single flag permanently disqualifies only the signature it
    /// was recorded against, never diluted by volume and never bleeding into an unrelated
    /// signature under the same owner.
    /// </summary>
    Task<bool> HasDuplicateAssociationAsync(
        string ownerId,
        string signatureHash,
        CancellationToken cancellationToken = default);
}
