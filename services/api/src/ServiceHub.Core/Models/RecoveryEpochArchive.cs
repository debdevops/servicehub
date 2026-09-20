using ServiceHub.Core.DTOs.Responses;

namespace ServiceHub.Core.Models;

/// <summary>
/// The on-disk shape of one sealed Recovery Evidence Ledger epoch (roadmap next-chapter M5.2) —
/// written by <c>Infrastructure.RecoveryLedger.RecoveryEpochArchiveService</c> and read by both
/// it and <c>scripts/verify-recovery-chain.py</c>'s <c>--archive-dir</c> mode. Serialized with
/// camelCase property names so the same field names the Python verifier already expects
/// (<c>startSeq</c>, <c>startPrevHash</c>, <c>terminalHash</c>, <c>events</c>) apply here too —
/// deliberately not reusing <c>RecoveryEvidenceExport</c>'s shape, since an archive has no
/// manifest, no honesty statements, and is scoped to one owner's one epoch rather than one
/// operation.
/// </summary>
public sealed class RecoveryEpochArchive
{
    /// <summary>The owner this epoch belongs to.</summary>
    public required string OwnerId { get; init; }

    /// <summary>1-based, monotonically increasing per owner. Epoch 1 always starts at Seq 1 with
    /// <see cref="StartPrevHash"/> equal to the genesis hash; every later epoch's
    /// <see cref="StartPrevHash"/> equals the previous epoch's <see cref="TerminalHash"/>.</summary>
    public required int EpochNumber { get; init; }

    /// <summary>The lowest <c>Seq</c> archived in this epoch — always matches
    /// <see cref="Events"/>'s first element.</summary>
    public required long StartSeq { get; init; }

    /// <summary>The <c>PrevHash</c> of <see cref="Events"/>'s first element — this epoch's own
    /// anchor into whatever came before it.</summary>
    public required string StartPrevHash { get; init; }

    /// <summary>The highest <c>Seq</c> archived in this epoch — always matches
    /// <see cref="Events"/>'s last element, and always one less than the
    /// <see cref="Enums.RecoveryEventType.EpochSealed"/> marker's own <c>Seq</c>, which stays
    /// live rather than being archived.</summary>
    public required long EndSeq { get; init; }

    /// <summary>The <c>EntryHash</c> of <see cref="Events"/>'s last element — the value the next
    /// epoch (or, for the most recent epoch, the live table's surviving seal marker) anchors to.</summary>
    public required string TerminalHash { get; init; }

    /// <summary>When this epoch was sealed.</summary>
    public required DateTimeOffset SealedAtUtc { get; init; }

    /// <summary>Every event archived under this epoch, Seq-ordered — a complete, independently
    /// re-verifiable copy of what was pruned from the live table.</summary>
    public required IReadOnlyList<RecoveryEventResponse> Events { get; init; }
}
