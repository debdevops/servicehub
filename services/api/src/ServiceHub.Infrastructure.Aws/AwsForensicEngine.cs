using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Forensic analysis engine for AWS SQS dead-letter messages.
/// Evaluates AWS-specific rules (via <see cref="AwsForensicExtensions"/>) before delegating
/// to the base three-tier forensic engine.
/// </summary>
public sealed class AwsForensicEngine : IForensicEngine
{
    private readonly IForensicEngine _baseEngine;
    private readonly ILogger<AwsForensicEngine> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="AwsForensicEngine"/>.
    /// </summary>
    /// <param name="baseEngine">
    /// The base forensic engine (<see cref="ServiceHub.Infrastructure.AI.ForensicEngine"/>).
    /// Must be registered separately — this class wraps it rather than inheriting from it.
    /// </param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsForensicEngine(IForensicEngine baseEngine, ILogger<AwsForensicEngine> logger)
    {
        _baseEngine = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyses a DLQ message by first running AWS-specific rules, then falling through
    /// to the base three-tier engine (Deterministic → Heuristic → Unknown).
    /// </summary>
    /// <param name="msg">The dead-letter message to analyse.</param>
    /// <returns>A <see cref="ForensicEngineResult"/> with the determined category, confidence, and replay safety.</returns>
    public ForensicEngineResult Analyse(DlqMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        // AWS-specific tier — runs before the base engine's deterministic rules.
        var awsHit = AwsForensicExtensions.Evaluate(msg);
        if (awsHit is not null)
        {
            // Replay safety is always Unsafe for SQS DLQ messages —
            // replaying before root cause is confirmed risks re-poisoning.
            const string replaySafety = "ManualReviewRequired";

            _logger.LogDebug(
                "AwsForensic hit for message {MessageId}: {Category} ({Confidence:P0}) — {RootCause}",
                msg.MessageId, awsHit.Category, awsHit.Confidence, awsHit.RootCause);

            return new ForensicEngineResult(
                awsHit.Category,
                awsHit.Confidence,
                awsHit.RootCause,
                replaySafety,
                "AWS-Deterministic");
        }

        // Fall through to base engine (Azure-oriented deterministic + heuristic + unknown).
        _logger.LogDebug(
            "AwsForensic: no AWS rule matched for message {MessageId}, delegating to base engine",
            msg.MessageId);

        return _baseEngine.Analyse(msg);
    }
}
