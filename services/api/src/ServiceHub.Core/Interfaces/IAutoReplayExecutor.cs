using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Executes auto-replay actions against DLQ messages that matched a rule.
/// Handles the actual Service Bus replay, rate limiting, and replay-history recording.
/// </summary>
public interface IAutoReplayExecutor
{
    /// <summary>
    /// Executes a matched rule against a DLQ message by replaying it.
    /// </summary>
    /// <param name="message">The DLQ message to replay.</param>
    /// <param name="rule">The matched rule.</param>
    /// <param name="action">The parsed action configuration.</param>
    /// <param name="ns">
    /// The namespace <paramref name="message"/> belongs to — already loaded by the caller
    /// (<c>DlqMonitorWorker.EvaluateAutoReplayRulesAsync</c>), passed through rather than
    /// re-resolved here, for the recovery ledger entry's namespace/provider/environment snapshot.
    /// </param>
    /// <param name="operationId">
    /// The <see cref="Entities.RecoveryOperation"/> this replay's <see cref="Entities.RecoveryLedgerEntry"/>
    /// belongs to. One operation covers every message this rule replays within one scan cycle —
    /// the caller opens it once per firing rule and reuses it across messages, rather than this
    /// method opening one per message.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with outcome description, or failure.</returns>
    Task<Result<string>> ExecuteAsync(
        DlqMessage message,
        AutoReplayRule rule,
        RuleAction action,
        Namespace ns,
        Guid operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the given rule has exceeded its per-hour replay limit.
    /// </summary>
    /// <param name="ruleId">The rule ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the rule can still replay (under the limit).</returns>
    Task<bool> CanReplayAsync(long ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the owner's combined automated replay rate, across every one of their
    /// auto-replay rules, is still under <c>RecoveryEvidence:FleetReplayVelocityCapPerHour</c> —
    /// the blast-radius cap that <see cref="CanReplayAsync"/> cannot see, since it only bounds
    /// one rule at a time. Several individually-reasonable per-rule limits can otherwise sum to
    /// a much larger aggregate replay volume than any single rule's cap implies.
    /// </summary>
    /// <param name="ownerId">The owner whose rules are being aggregated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the owner's fleet-wide replay rate is still under the cap.</returns>
    Task<bool> CanReplayFleetWideAsync(string ownerId, CancellationToken cancellationToken = default);
}
