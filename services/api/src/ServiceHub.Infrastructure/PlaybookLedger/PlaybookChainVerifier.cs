using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// Recomputes and compares one owner's Playbook Ledger hash chain. Pure and DB-agnostic — takes an
/// already-loaded, <see cref="PlaybookEvent.Seq"/>-ordered list, so it's unit-testable with plain
/// in-memory data. Mirrors <c>RecoveryChainVerifier</c> exactly, independently, for the Playbook
/// chain.
/// </summary>
/// <remarks>
/// Tamper-EVIDENT, not tamper-PROOF — same honest framing as the Recovery Evidence Ledger.
/// </remarks>
public static class PlaybookChainVerifier
{
    /// <summary>
    /// Verifies <paramref name="events"/>, which must already be filtered to a single owner and
    /// ordered by <see cref="PlaybookEvent.Seq"/> ascending.
    /// </summary>
    public static ChainVerificationResult Verify(string ownerId, IReadOnlyList<PlaybookEvent> events)
    {
        var expectedPrevHash = PlaybookHashChain.GenesisHash;
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

            var recomputedHash = PlaybookHashChain.ComputeEntryHash(
                evt.Id, evt.OwnerId, evt.Seq, evt.EntryId, evt.EventType,
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
