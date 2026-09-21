using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Three-tier forensic analysis engine for DLQ messages.
/// Pipeline: Deterministic → Heuristic → (future OpenAI) → ReplaySafety.
/// </summary>
public sealed class ForensicEngine : IForensicEngine
{
    private readonly ILogger<ForensicEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForensicEngine"/> class.
    /// </summary>
    public ForensicEngine(ILogger<ForensicEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ForensicEngineResult Analyse(DlqMessage msg)
    {
        // Tier 1 – Deterministic
        var detHit = DeterministicClassifier.Evaluate(msg);
        if (detHit is not null)
        {
            var safety = ReplaySafetyClassifier.Classify(msg, detHit.Category);
            _logger.LogDebug(
                "Forensic Tier-1 hit for message {MessageId}: {Category} ({Confidence:P0})",
                msg.MessageId, detHit.Category, detHit.Confidence);
            return new ForensicEngineResult(detHit.Category, detHit.Confidence, detHit.RootCause, safety, "Deterministic");
        }

        // Tier 2 – Heuristic
        var heuHit = HeuristicAnalyser.Evaluate(msg);
        if (heuHit is not null)
        {
            var safety = ReplaySafetyClassifier.Classify(msg, heuHit.Category);
            _logger.LogDebug(
                "Forensic Tier-2 hit for message {MessageId}: {Category} ({Confidence:P0})",
                msg.MessageId, heuHit.Category, heuHit.Confidence);
            return new ForensicEngineResult(heuHit.Category, heuHit.Confidence, heuHit.RootCause, safety, "Heuristic");
        }

        // Tier 3 – Placeholder for future OpenAI integration
        // Falls through to Unknown
        _logger.LogDebug("Forensic: no tier matched for message {MessageId}", msg.MessageId);
        var unknownSafety = ReplaySafetyClassifier.Classify(msg, FailureCategory.Unknown);
        return new ForensicEngineResult(
            FailureCategory.Unknown,
            0.0,
            "No deterministic or heuristic rule matched — manual review recommended.",
            unknownSafety,
            "None");
    }
}
