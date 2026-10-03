using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Seals and archives one Recovery Evidence Ledger epoch for an owner (roadmap next-chapter
/// M5.2) — the operation that bounds the live <c>RecoveryEvents</c> table's growth for
/// multi-year operation without weakening its tamper-evidence: every archived event's content
/// durably survives, byte for byte, in a verified file on disk before it is ever pruned from the
/// live table.
/// </summary>
public interface IRecoveryEpochArchiveService
{
    /// <summary>
    /// Seals the owner's current epoch (via <see cref="IRecoveryLedger.SealEpochAsync"/>),
    /// writes every event since the previous seal (or genesis, for the first epoch) to a new
    /// archive file, verifies that file independently from disk, and only then prunes those
    /// rows from the live table. The epoch's own <c>EpochSealed</c> marker event is deliberately
    /// never archived itself — it stays live as the anchor the next epoch continues from.
    /// Fails, and changes nothing, if there is nothing new to seal or if the range to be
    /// archived does not itself verify.
    /// </summary>
    Task<Result<RecoveryEpochSealSummary>> SealAndArchiveEpochAsync(
        string ownerId,
        RecoveryActor actor,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of one <see cref="IRecoveryEpochArchiveService.SealAndArchiveEpochAsync"/> call.</summary>
public sealed record RecoveryEpochSealSummary(
    int EpochNumber,
    long StartSeq,
    long EndSeq,
    string TerminalHash,
    string ArchiveFilePath,
    int ArchivedEventCount);
