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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with outcome description, or failure.</returns>
    Task<Result<string>> ExecuteAsync(
        DlqMessage message,
        AutoReplayRule rule,
        RuleAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the given rule has exceeded its per-hour replay limit.
    /// </summary>
    /// <param name="ruleId">The rule ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the rule can still replay (under the limit).</returns>
    Task<bool> CanReplayAsync(long ruleId, CancellationToken cancellationToken = default);
}
