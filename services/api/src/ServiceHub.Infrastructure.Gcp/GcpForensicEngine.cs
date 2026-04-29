using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Forensic analysis engine for GCP Pub/Sub dead-letter messages.
/// Evaluates GCP-specific rules (via <see cref="GcpForensicExtensions"/>) before delegating
/// to the base three-tier forensic engine.
/// </summary>
public sealed class GcpForensicEngine : IForensicEngine
{
    private readonly IForensicEngine _baseEngine;
    private readonly ILogger<GcpForensicEngine> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="GcpForensicEngine"/>.
    /// </summary>
    /// <param name="baseEngine">
    /// The base forensic engine. Must be registered separately — this class wraps it.
    /// </param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public GcpForensicEngine(IForensicEngine baseEngine, ILogger<GcpForensicEngine> logger)
    {
        _baseEngine = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyses a DLQ message by first running GCP-specific rules, then falling through
    /// to the base three-tier engine (Deterministic → Heuristic → Unknown).
    /// </summary>
    /// <param name="msg">The dead-letter message to analyse.</param>
    /// <returns>A <see cref="ForensicEngineResult"/> with the determined category, confidence, and replay safety.</returns>
    public ForensicEngineResult Analyse(DlqMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        // GCP-specific tier — runs before the base engine's deterministic rules.
        var gcpHit = GcpForensicExtensions.Evaluate(msg);
        if (gcpHit is not null)
        {
            const string replaySafety = "ManualReviewRequired";

            _logger.LogDebug(
                "GcpForensic hit for message {MessageId}: {Category} ({Confidence:P0}) — {RootCause}",
                msg.MessageId, gcpHit.Category, gcpHit.Confidence, gcpHit.RootCause);

            return new ForensicEngineResult(
                gcpHit.Category,
                gcpHit.Confidence,
                gcpHit.RootCause,
                replaySafety,
                "GCP-Deterministic");
        }

        // Fall through to base engine.
        _logger.LogDebug(
            "GcpForensic: no GCP rule matched for message {MessageId}, delegating to base engine",
            msg.MessageId);

        return _baseEngine.Analyse(msg);
    }
}
