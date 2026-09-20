using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Enforces the Playbook Ledger's append-only/immutability invariants — sibling to
/// <see cref="RecoveryLedgerAppendOnlyGuard"/>, invoked from the same <c>DlqDbContext.SaveChanges</c>/
/// <c>SaveChangesAsync</c> overrides, alongside (not replacing) the Recovery guard. One guard per
/// ledger, never a single guard branching on table name, so each ledger's own mutable-property
/// allow-list stays independently auditable.
/// </summary>
public static class PlaybookLedgerAppendOnlyGuard
{
    public static readonly IReadOnlySet<string> MutableEntryProperties = new HashSet<string>
    {
        nameof(PlaybookEntry.State),
        nameof(PlaybookEntry.Disposition),
        nameof(PlaybookEntry.LastEventSeq),
        nameof(PlaybookEntry.ClosedAt),
    };

    public static void Enforce(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case PlaybookEvent when entry.State is EntityState.Deleted:
                    throw new InvalidOperationException(
                        "Playbook ledger violation: PlaybookEvent rows are append-only and cannot be deleted.");

                case PlaybookEvent when entry.State is EntityState.Modified:
                    throw new InvalidOperationException(
                        "Playbook ledger violation: PlaybookEvent rows are append-only and cannot be modified after insert.");

                case PlaybookEntry when entry.State is EntityState.Deleted:
                    throw new InvalidOperationException(
                        "Playbook ledger violation: PlaybookEntry rows cannot be deleted.");

                case PlaybookEntry when entry.State is EntityState.Modified:
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
                "Playbook ledger violation: PlaybookEntry propert"
                + (offendingProperties.Count == 1 ? "y" : "ies")
                + $" [{string.Join(", ", offendingProperties)}] cannot be modified after insert.");
        }
    }
}
