using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Engine that evaluates auto-replay rules against DLQ messages.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Evaluates a list of conditions against a single DLQ message.
    /// All conditions must match (AND logic).
    /// </summary>
    /// <param name="message">The DLQ message to evaluate.</param>
    /// <param name="conditions">The conditions to test.</param>
    /// <returns>A match result indicating whether all conditions matched.</returns>
    RuleMatchResult Evaluate(DlqMessage message, IReadOnlyList<RuleCondition> conditions);

    /// <summary>
    /// Evaluates a rule against a batch of DLQ messages.
    /// </summary>
    /// <param name="messages">The messages to evaluate.</param>
    /// <param name="conditions">The conditions to test.</param>
    /// <returns>Match results for each message.</returns>
    IReadOnlyList<RuleMatchResult> EvaluateBatch(
        IReadOnlyList<DlqMessage> messages,
        IReadOnlyList<RuleCondition> conditions);

    /// <summary>
    /// Finds all enabled rules that match a given DLQ message.
    /// </summary>
    /// <param name="message">The DLQ message.</param>
    /// <param name="rules">Available rules to evaluate.</param>
    /// <returns>Matched rules with their parsed conditions/actions.</returns>
    IReadOnlyList<(AutoReplayRule Rule, RuleAction Action)> FindMatchingRules(
        DlqMessage message,
        IReadOnlyList<AutoReplayRule> rules);
}
