using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Reconciles the two transient claim states — <see cref="DlqMessageStatus.Replaying"/> and
/// <see cref="DlqMessageStatus.Purging"/> — left behind by a process that died mid-operation.
/// </summary>
/// <remarks>
/// <para>
/// Every mutating path (<c>BulkOperationExecutor</c> replay and purge,
/// <c>SignatureReplayExecutor</c>, <c>AutoReplayExecutor</c>) commits its claim <em>before</em>
/// calling the live provider, deliberately: <c>DlqMessage.Status</c> is an EF concurrency token,
/// so the committed claim is what stops a second worker acting on the same message twice. The
/// cost is a window, as long as the provider call, in which a crash leaves the claim committed.
/// </para>
/// <para>
/// Nothing previously reconciled that. Eligibility is <c>Active</c> or <c>ReplayFailed</c>, so an
/// abandoned claim made the message permanently ineligible for bulk replay, signature replay,
/// bulk purge and auto-replay rules alike, with no UI affordance to recover it.
/// </para>
/// <para>
/// <strong>Why this runs at startup rather than inside the replay workers.</strong> It must only
/// ever observe abandoned claims, never rows a live executor is actively working. Running it
/// before any hosted service has started makes that guarantee structural: no executor exists yet,
/// so every claimed row is by definition left over from a previous process. Putting the same
/// sweep inside a worker's restart-recovery would race <c>AutoReplayExecutor</c>, which is driven
/// by <c>DlqMonitorWorker</c> on its own schedule — resetting a genuinely in-flight replay would
/// be a worse defect than the one being fixed.
/// </para>
/// <para>
/// The two states recover differently, because what is unknown differs:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <c>Replaying</c> becomes <c>ReplayFailed</c>. The provider may well have accepted the
///     message before the process died, so the outcome is genuinely unknown; <c>ReplayFailed</c>
///     is replay-eligible so the operator regains control, and the <see cref="ReplayHistory"/>
///     entry records the ambiguity rather than asserting a success or failure never observed.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>Purging</c> becomes <c>Active</c>. Claiming a purge asserts nothing about the message
///     having left the DLQ, and <c>Discarded</c> would falsely claim it had. If the purge did in
///     fact succeed, the next <c>DlqMonitorService</c> scan observes the message is gone and
///     resolves it through the normal path.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class InterruptedOperationRecovery
{
    internal const string InterruptedOutcome = "Interrupted";

    internal const string InterruptedExplanation =
        "Interrupted by a server restart while the replay was in flight. The message may or may " +
        "not have reached the destination entity — verify before replaying it again.";

    /// <summary>
    /// Transitions every message left in a transient claim state back to a recoverable one, and
    /// records an explanatory <see cref="ReplayHistory"/> entry for each interrupted replay.
    /// </summary>
    /// <param name="dbContext">The DLQ database context.</param>
    /// <param name="recoveryLedger">
    /// The Recovery Evidence Ledger — used to close any <see cref="RecoveryLedgerEntry"/> left in
    /// <see cref="RecoveryEntryState.Executing"/> by the same crash, as
    /// <see cref="RecoveryEntryState.ExecutionUnknown"/>. This is the one legitimate use of
    /// that outcome in the whole Recovery Evidence Ledger: every other path knows whether the
    /// provider accepted or rejected the call; only a process death mid-call genuinely doesn't.
    /// </param>
    /// <param name="logger">Logger for the recovery summary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of messages reconciled.</returns>
    public static async Task<int> ReconcileInterruptedOperationsAsync(
        DlqDbContext dbContext,
        IRecoveryLedger recoveryLedger,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(recoveryLedger);
        ArgumentNullException.ThrowIfNull(logger);

        var stranded = await dbContext.DlqMessages
            .Where(m => m.Status == DlqMessageStatus.Replaying
                        || m.Status == DlqMessageStatus.Purging)
            .ToListAsync(cancellationToken);

        if (stranded.Count == 0)
        {
            return 0;
        }

        var recoveredAt = DateTimeOffset.UtcNow;
        var replayCount = 0;
        var purgeCount = 0;

        foreach (var message in stranded)
        {
            if (message.Status == DlqMessageStatus.Purging)
            {
                message.Status = DlqMessageStatus.Active;
                purgeCount++;
                continue;
            }

            message.Status = DlqMessageStatus.ReplayFailed;

            // Deliberately left null rather than set to false: false would assert the replay
            // definitely did not happen, which is exactly what we do not know.
            message.ReplaySuccess = null;
            replayCount++;

            dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = message.Id,
                ReplayedAt = recoveredAt,
                ReplayedBy = "startup-recovery",
                ReplayStrategy = "original-entity",
                ReplayedToEntity = message.EntityName,
                OutcomeStatus = InterruptedOutcome,
                ErrorDetails = InterruptedExplanation,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Close out any RecoveryLedgerEntry the same crash left stranded in Executing — the
        // ledger's own record of "a claim was committed but we never learned the provider's
        // answer," parallel to the DlqMessage reconciliation above. DlqMessageId is a soft
        // reference (no FK — see RecoveryLedgerEntry), so this is a plain query, not a join the
        // schema enforces.
        var strandedIds = stranded.Select(m => m.Id).ToHashSet();
        var orphanedEntries = await dbContext.RecoveryLedgerEntries
            .Where(e => e.DlqMessageId != null
                        && strandedIds.Contains(e.DlqMessageId!.Value)
                        && e.State == RecoveryEntryState.Executing)
            .ToListAsync(cancellationToken);

        var actor = ActorIdentityResolver.ResolveSystemActor("startup-recovery");
        var closedLedgerEntries = 0;

        foreach (var entry in orphanedEntries)
        {
            var result = await recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = entry.OwnerId,
                Actor = actor,
                Outcome = RecoveryExecutionOutcome.Unknown,
            }, cancellationToken);

            if (result.IsSuccess)
            {
                closedLedgerEntries++;
            }
            else
            {
                // Not fatal to startup — an entry left at Executing is honestly ambiguous, not
                // silently wrong, and a later restart's sweep gets another chance to close it.
                logger.LogWarning(
                    "Failed to close orphaned recovery ledger entry {EntryId} as ExecutionUnknown: {Error}",
                    entry.Id, result.Error.Message);
            }
        }

        logger.LogWarning(
            "Recovered {Total} DLQ message(s) stranded by a prior restart: {ReplayCount} " +
            "interrupted replay(s) are now ReplayFailed and carry a replay-history entry recording " +
            "that the original outcome is unknown; {PurgeCount} interrupted purge(s) are now Active; " +
            "{LedgerCount} orphaned recovery ledger entr{Plural} closed as ExecutionUnknown.",
            stranded.Count,
            replayCount,
            purgeCount,
            closedLedgerEntries,
            closedLedgerEntries == 1 ? "y" : "ies");

        return stranded.Count;
    }
}
