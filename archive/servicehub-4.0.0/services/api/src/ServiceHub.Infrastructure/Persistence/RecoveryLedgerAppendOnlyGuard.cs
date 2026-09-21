using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Enforces the Recovery Evidence Ledger's append-only invariant at the persistence layer,
/// independent of caller discipline. Invoked from <see cref="DlqDbContext.SaveChanges"/> and
/// <see cref="DlqDbContext.SaveChangesAsync"/> before every save.
/// </summary>
public static class RecoveryLedgerAppendOnlyGuard
{
    /// <summary>
    /// <see cref="RecoveryLedgerEntry"/> properties writable after insert, only through
    /// <c>IRecoveryLedger</c>, alongside the <see cref="RecoveryEvent"/> that justifies the
    /// change. Every other property on the three ledger entity types is immutable once saved.
    /// Declared once here so both the enforcement below and a reflection completeness test
    /// (asserting every entry property is classified) share a single source of truth.
    /// </summary>
    public static readonly IReadOnlySet<string> MutableEntryProperties = new HashSet<string>
    {
        nameof(RecoveryLedgerEntry.State),
        nameof(RecoveryLedgerEntry.Disposition),
        nameof(RecoveryLedgerEntry.VerificationResult),
        nameof(RecoveryLedgerEntry.VerificationConfidence),
        nameof(RecoveryLedgerEntry.ObservationWindowEndsAt),
        nameof(RecoveryLedgerEntry.LastEventSeq),
        nameof(RecoveryLedgerEntry.ClosedAt),
        nameof(RecoveryLedgerEntry.RecoveryMarker),
        nameof(RecoveryLedgerEntry.MarkerApplied),
    };

    /// <summary>
    /// Inspects the tracked changes for the three ledger entity types and throws
    /// <see cref="InvalidOperationException"/> on any violation: deleting any of them, modifying
    /// a <see cref="RecoveryOperation"/> or <see cref="RecoveryEvent"/> at all, or modifying a
    /// <see cref="RecoveryLedgerEntry"/> property outside <see cref="MutableEntryProperties"/>.
    /// A cheap early-return keeps this a no-op for saves that touch none of the three types, so
    /// every other entity's save path is unaffected.
    /// </summary>
    public static void Enforce(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case RecoveryOperation when entry.State is EntityState.Deleted:
                    throw new InvalidOperationException(
                        "Recovery ledger violation: RecoveryOperation rows are immutable and cannot be deleted.");

                case RecoveryOperation when entry.State is EntityState.Modified:
                    throw new InvalidOperationException(
                        "Recovery ledger violation: RecoveryOperation rows are immutable and cannot be modified after insert.");

                case RecoveryEvent when entry.State is EntityState.Deleted:
                    throw new InvalidOperationException(
                        "Recovery ledger violation: RecoveryEvent rows are append-only and cannot be deleted.");

                case RecoveryEvent when entry.State is EntityState.Modified:
                    throw new InvalidOperationException(
                        "Recovery ledger violation: RecoveryEvent rows are append-only and cannot be modified after insert.");

                case RecoveryLedgerEntry when entry.State is EntityState.Deleted:
                    throw new InvalidOperationException(
                        "Recovery ledger violation: RecoveryLedgerEntry rows cannot be deleted.");

                case RecoveryLedgerEntry when entry.State is EntityState.Modified:
                    EnforceEntryImmutableProperties(entry);
                    break;
            }
        }
    }

    private static void EnforceEntryImmutableProperties(EntityEntry entry)
    {
        var offendingProperties = entry.Properties
            .Where(p => p.IsModified && !MutableEntryProperties.Contains(p.Metadata.Name))
            .Select(p => p.Metadata.Name)
            .ToList();

        if (offendingProperties.Count > 0)
        {
            throw new InvalidOperationException(
                "Recovery ledger violation: RecoveryLedgerEntry propert"
                + (offendingProperties.Count == 1 ? "y" : "ies")
                + $" [{string.Join(", ", offendingProperties)}] cannot be modified after insert.");
        }
    }
}
