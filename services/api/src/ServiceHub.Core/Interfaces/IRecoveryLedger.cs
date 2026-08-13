using ServiceHub.Core.Entities;
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
/// <see cref="Result{T}"/> failure, never an exception. This interface currently has no callers —
/// no controller, executor, or worker is wired to it yet.
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
}
