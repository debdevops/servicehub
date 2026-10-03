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
    /// The autonomy grant for one (owner, signature, action), or null when none was ever created — in which case the
    /// gate escalates every unattended action, which is the honest answer while nothing has earned it (unit 4.1).
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

    // ── Trust and autonomy (unit 4.1) — every number is a deterministic query over recorded outcomes ──

    /// <summary>Terminal dispositions of one signature's entries for one action kind.</summary>
    Task<IReadOnlyDictionary<Enums.RecoveryDisposition, int>> GetDispositionCountsAsync(
        string ownerId, string signatureHash, Enums.RecoveryOperationKind actionKind, CancellationToken cancellationToken = default);

    /// <summary>The signatures that have entries of one action kind, in a stable order, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<string>> GetDistinctSignatureHashesAsync(
        string ownerId, Enums.RecoveryOperationKind actionKind, int limit = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>The cloud a signature belongs to — from its entries, else from where it was last seen; null if unknown.</summary>
    Task<Enums.CloudProviderType?> GetSignatureProviderAsync(string ownerId, string signatureHash, CancellationToken cancellationToken = default);

    /// <summary>The environment a signature belongs to — from its entries, else from where it was last seen; null if unknown.</summary>
    Task<Enums.EnvironmentType?> GetSignatureEnvironmentAsync(string ownerId, string signatureHash, CancellationToken cancellationToken = default);

    /// <summary>The namespace a signature belongs to — from its entries, else from where it was last seen; null if unknown.</summary>
    Task<Guid?> GetSignatureNamespaceIdAsync(string ownerId, string signatureHash, CancellationToken cancellationToken = default);

    /// <summary>Whether a person flagged an unsafe outcome anywhere in this owner's fleet.</summary>
    Task<bool> HasUnsafeOutcomeFlagAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Whether a person flagged a duplicate business effect on one of this signature's entries.</summary>
    Task<bool> HasDuplicateAssociationAsync(string ownerId, string signatureHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a grant from <paramref name="previousLevel"/> to <paramref name="newLevel"/> and, in the same save,
    /// appends a hash-chained promoted/demoted event. A Conflict when another writer already moved it.
    /// </summary>
    Task<Result<AutonomyGrant>> RecordAutonomyGrantTransitionAsync(
        string ownerId, string signatureHash, Enums.RecoveryOperationKind actionKind,
        Enums.AutonomyLevel previousLevel, Enums.AutonomyLevel newLevel, string reason, string? evidenceJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the gate refused an automatic action and a person must decide (unit 5.1): a terminal entry with
    /// disposition Declined plus one <c>EligibilityDeclined</c> event carrying the reason code. Nothing reaches a cloud.
    /// </summary>
    Task<Result<RecoveryLedgerEntry>> RecordDeclinedAsync(BeginRecoveryEntryRequest request, string reasonCode, string? detailJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// A person answered what the agent asked (units 5.10): appends an <c>OperatorNote</c> event to the Declined entry with the
    /// decision — <c>approved</c> (with the replay entry it caused) or <c>declined</c> (with a required reason). Nothing is
    /// deleted; the entry stops being pending work. A second answer to the same entry is a Conflict.
    /// </summary>
    Task<Result<RecoveryEvent>> RecordDecisionAsync(Guid entryId, string ownerId, RecoveryActor actor, bool approved, string? reason, Guid? replayEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches emergency stop on or off for an owner (unit 6.10): a hash-chained EmergencyStopActivated / Cleared event.
    /// While on, the eligibility gate lets nothing act on its own. Copied from 4.0.0.
    /// </summary>
    Task<Result<RecoveryOperation>> RecordEmergencyControlEventAsync(string ownerId, RecoveryActor actor, bool activate, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Whether emergency stop is on, and if so who switched it on, when and why — from the latest control event.</summary>
    Task<EmergencyStopState> GetEmergencyStopStateAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Every grant of one owner.</summary>
    Task<IReadOnlyList<AutonomyGrant>> GetAutonomyGrantsAsync(string ownerId, CancellationToken cancellationToken = default);
}

/// <summary>Emergency stop, as the banner shows it.</summary>
/// <param name="Active">Whether it is on.</param>
/// <param name="By">Who switched it on (or last off).</param>
/// <param name="At">When.</param>
/// <param name="Reason">Why, as they wrote it.</param>
public sealed record EmergencyStopState(bool Active, string? By, DateTimeOffset? At, string? Reason);
