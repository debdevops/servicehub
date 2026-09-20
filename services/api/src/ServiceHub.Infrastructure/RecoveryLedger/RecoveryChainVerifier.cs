using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// Recomputes and compares one owner's Recovery Evidence Ledger hash chain. Pure and
/// DB-agnostic — takes an already-loaded, <see cref="RecoveryEvent.Seq"/>-ordered list, so it's
/// unit-testable with plain in-memory data and reusable regardless of how the caller loaded the
/// events.
/// </summary>
/// <remarks>
/// This is tamper-EVIDENT, not tamper-PROOF: anyone with write access to the underlying SQLite
/// file can recompute the entire chain. Verification here detects casual or partial alteration —
/// a modified event, an incorrect <see cref="RecoveryEvent.PrevHash"/>/<see cref="RecoveryEvent.EntryHash"/>,
/// or a gap in <see cref="RecoveryEvent.Seq"/> — nothing more.
/// </remarks>
public static class RecoveryChainVerifier
{
    /// <summary>
    /// Verifies <paramref name="events"/>, which must already be filtered to a single owner and
    /// ordered by <see cref="RecoveryEvent.Seq"/> ascending. Returns the first <c>Seq</c> at
    /// which the chain diverges, if any.
    /// </summary>
    /// <param name="ownerId">The owner whose chain is being verified.</param>
    /// <param name="events">The events to verify, Seq-ascending.</param>
    /// <param name="startingSeq">The <c>Seq</c> the first element of <paramref name="events"/> is
    /// expected to carry. Defaults to 1 (a chain's true genesis). A sealed epoch's archive
    /// (roadmap next-chapter M5.2) passes its own first archived event's actual <c>Seq</c> here,
    /// since an archive after the first epoch never starts at 1.</param>
    /// <param name="startingPrevHash">The <c>PrevHash</c> the first element of
    /// <paramref name="events"/> is expected to carry. Defaults to
    /// <see cref="RecoveryHashChain.GenesisHash"/>. An archive after the first epoch passes the
    /// previous epoch's terminal hash instead — the anchor this range chains from.</param>
    public static ChainVerificationResult Verify(
        string ownerId,
        IReadOnlyList<RecoveryEvent> events,
        long startingSeq = 1,
        string? startingPrevHash = null)
    {
        var expectedPrevHash = startingPrevHash ?? RecoveryHashChain.GenesisHash;
        var expectedSeq = startingSeq;
        var checkedCount = 0;

        foreach (var evt in events)
        {
            checkedCount++;

            if (evt.Seq != expectedSeq)
            {
                return Invalid(ownerId, checkedCount, evt.Seq,
                    $"Sequence gap: expected Seq {expectedSeq} but found {evt.Seq}.");
            }

            if (!string.Equals(evt.PrevHash, expectedPrevHash, StringComparison.Ordinal))
            {
                return Invalid(ownerId, checkedCount, evt.Seq,
                    $"PrevHash mismatch at Seq {evt.Seq}: expected {expectedPrevHash}, found {evt.PrevHash}.");
            }

            var recomputedHash = RecoveryHashChain.ComputeEntryHash(
                evt.Id, evt.OwnerId, evt.Seq, evt.EntryId, evt.OperationId, evt.EventType,
                evt.OccurredAt, evt.ActorIdentity, evt.ActorKind, evt.DetailJson, evt.SchemaVersion,
                expectedPrevHash);

            if (!string.Equals(recomputedHash, evt.EntryHash, StringComparison.Ordinal))
            {
                return Invalid(ownerId, checkedCount, evt.Seq,
                    $"EntryHash mismatch at Seq {evt.Seq}: the stored event does not match its recomputed hash — evidence was modified after it was appended.");
            }

            expectedPrevHash = evt.EntryHash;
            expectedSeq++;
        }

        return new ChainVerificationResult
        {
            OwnerId = ownerId,
            IsValid = true,
            EventsChecked = events.Count,
            FirstDivergentSeq = null,
            Reason = null
        };
    }

    private static ChainVerificationResult Invalid(string ownerId, int eventsChecked, long divergentSeq, string reason)
        => new()
        {
            OwnerId = ownerId,
            IsValid = false,
            EventsChecked = eventsChecked,
            FirstDivergentSeq = divergentSeq,
            Reason = reason
        };
}
