using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Three-tier forensic analysis engine for DLQ messages.
/// Pipeline: Deterministic → Heuristic → (future OpenAI) → ReplaySafety.
/// </summary>
public sealed class ForensicEngine
{
    private readonly ILogger<ForensicEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForensicEngine"/> class.
    /// </summary>
    public ForensicEngine(ILogger<ForensicEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Result of a forensic analysis run.
    /// </summary>
    public sealed record ForensicResult(
        FailureCategory Category,
        double Confidence,
        string RootCause,
        string ReplaySafety,
        string Tier);

    /// <summary>
    /// Analyses a single DLQ message through the three-tier pipeline.
    /// </summary>
    public ForensicResult Analyse(DlqMessage msg)
    {
        // Tier 1 – Deterministic
        var detHit = DeterministicClassifier.Evaluate(msg);
        if (detHit is not null)
        {
            var safety = ReplaySafetyClassifier.Classify(msg, detHit.Category);
            _logger.LogDebug(
                "Forensic Tier-1 hit for message {MessageId}: {Category} ({Confidence:P0})",
                msg.MessageId, detHit.Category, detHit.Confidence);
            return new ForensicResult(detHit.Category, detHit.Confidence, detHit.RootCause, safety, "Deterministic");
        }

        // Tier 2 – Heuristic
        var heuHit = HeuristicAnalyser.Evaluate(msg);
        if (heuHit is not null)
        {
            var safety = ReplaySafetyClassifier.Classify(msg, heuHit.Category);
            _logger.LogDebug(
                "Forensic Tier-2 hit for message {MessageId}: {Category} ({Confidence:P0})",
                msg.MessageId, heuHit.Category, heuHit.Confidence);
            return new ForensicResult(heuHit.Category, heuHit.Confidence, heuHit.RootCause, safety, "Heuristic");
        }

        // Tier 3 – Placeholder for future OpenAI integration
        // Falls through to Unknown
        _logger.LogDebug("Forensic: no tier matched for message {MessageId}", msg.MessageId);
        var unknownSafety = ReplaySafetyClassifier.Classify(msg, FailureCategory.Unknown);
        return new ForensicResult(
            FailureCategory.Unknown,
            0.0,
            "No deterministic or heuristic rule matched — manual review recommended.",
            unknownSafety,
            "None");
    }
}
