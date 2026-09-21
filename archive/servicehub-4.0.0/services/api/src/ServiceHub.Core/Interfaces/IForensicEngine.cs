using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Result of a forensic analysis run.
/// </summary>
public sealed record ForensicEngineResult(
    FailureCategory Category,
    double Confidence,
    string RootCause,
    string ReplaySafety,
    string Tier);

/// <summary>
/// Three-tier forensic analysis engine for DLQ messages.
/// Implementations classify a message's failure root cause and replay safety.
/// </summary>
public interface IForensicEngine
{
    /// <summary>
    /// Analyses a single DLQ message through the forensic pipeline.
    /// </summary>
    ForensicEngineResult Analyse(DlqMessage msg);
}
