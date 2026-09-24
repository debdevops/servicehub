using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The sole writer of the Recovery Evidence Ledger — a durable, attributable, owner-isolated,
/// hash-chained record of every recovery decision ServiceHub makes and its eventual outcome. See
/// <see cref="RecoveryOperation"/>, <see cref="RecoveryLedgerEntry"/> and <see cref="RecoveryEvent"/>.
/// </summary>
/// <remarks>
/// No method here accepts a caller-supplied actor identity string — every actor enters as a
/// <see cref="RecoveryActor"/> resolved server-side. State transitions are enforced internally;
/// illegal transitions and owner mismatches return a <see cref="Result{T}"/> failure, never an
/// exception. 4.1.0 carries the core of 4.0.0's contract (unit 2.5): open, begin, execute, observe and
/// close. Declining, ageing, autonomy and epochs arrive with the units that need them.
/// </remarks>
public interface IRecoveryLedger
{
    /// <summary>Opens a new <see cref="RecoveryOperation"/> — the immutable header for one decision.</summary>
    Task<Result<RecoveryOperation>> OpenOperationAsync(OpenRecoveryOperationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Begins a <see cref="RecoveryLedgerEntry"/> under an open operation, in state <c>Executing</c>.</summary>
    Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(BeginRecoveryEntryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a provider call's outcome against an <c>Executing</c> entry: <c>Observing</c> (replay
    /// accepted), <c>Discarded</c> (purge accepted), <c>ExecutionFailed</c> (rejected) or
    /// <c>ExecutionUnknown</c> (outcome unknown).
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(RecordExecutionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what a dead-letter scan saw for an <c>Observing</c> entry: <c>Returned</c>,
    /// <c>Recovered</c> or <c>Unverified</c>.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(RecordObservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a non-terminal entry by an operator's declaration that it is unrecoverable
    /// (<c>WrittenOff</c>). Fails if the entry is already terminal or <paramref name="reason"/> is empty.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> CloseAsync(Guid entryId, string ownerId, RecoveryActor actor, string reason, CancellationToken cancellationToken = default);

    /// <summary>Recomputes one owner's hash chain and reports the first place it diverges, if any.</summary>
    Task<ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>One operation, or null when it does not exist or is another owner's.</summary>
    Task<RecoveryOperation?> GetOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>One entry, or null when it does not exist or is another owner's.</summary>
    Task<RecoveryLedgerEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Every event of one operation, in <c>Seq</c> order.</summary>
    Task<IReadOnlyList<RecoveryEvent>> GetEventsForOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every entry sharing the recurrence-lineage key (owner, namespace, entity, body hash) begun on or
    /// after <paramref name="since"/>, whatever its state — each is one past attempt (the eligibility
    /// gate's recurrence cap reads this).
    /// </summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> FindLineageMatchesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the owner's most recent emergency-stop event is an activation. A live query over the
    /// events — no stored flag and no second table.
    /// </summary>
    Task<bool> IsEmergencyStopActiveAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The autonomy grant for one (owner, signature, action), or null when none was ever created.
    /// <b>Always null until autonomy exists (unit 4.1)</b> — so the gate escalates every unattended
    /// action, which is the honest answer while nothing has earned it.
    /// </summary>
    Task<AutonomyGrant?> GetAutonomyGrantAsync(
        string ownerId, string signatureHash, Enums.RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The live production elevation covering a namespace, or null. <b>Always null: 4.1.0 ships no
    /// elevation</b>, so the gate denies Prod recovery (<c>PRODUCTION_ELEVATION_REQUIRED</c>) — closed,
    /// not open, until elevation is built.
    /// </summary>
    Task<ProductionElevation?> GetLiveProductionElevationAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>Every entry that is not terminal (executing, observing, unknown), oldest first — what still needs an outcome.</summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(string ownerId, int limit = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>The observing entry whose replay carried this <c>x-servicehub-recovery-id</c> — an exact match.</summary>
    Task<RecoveryLedgerEntry?> FindByMarkerAsync(string ownerId, string marker, CancellationToken cancellationToken = default);

    /// <summary>
    /// Observing entries a newly seen dead letter <i>might</i> be the return of, matched by queue and body hash because
    /// no marker survived: those replays whose marker was never applied and that began before <paramref name="beganBefore"/>.
    /// </summary>
    Task<IReadOnlyList<RecoveryLedgerEntry>> FindHeuristicRecurrenceCandidatesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset beganBefore,
        CancellationToken cancellationToken = default);
}
