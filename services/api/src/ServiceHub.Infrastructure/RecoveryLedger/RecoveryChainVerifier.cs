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
    public static ChainVerificationResult Verify(string ownerId, IReadOnlyList<RecoveryEvent> events)
    {
        var expectedPrevHash = RecoveryHashChain.GenesisHash;
        long expectedSeq = 1;
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
