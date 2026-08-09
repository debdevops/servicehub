namespace ServiceHub.Core.Models;

/// <summary>
/// Operational knowledge about a recurring failure.
///
/// FailureKnowledge captures institutional memory for each FailureSignature:
/// - What we know about why it happens
/// - How we fixed it before
/// - Who owns it
/// - Where to find the runbook
/// - Whether it's safe to replay
///
/// Knowledge is optional: not every signature has been investigated yet.
/// When knowledge exists, it enables engineers to resolve issues without
/// deep investigation, directly supporting the mission:
/// "Never investigate the same message failure twice."
///
/// Extensible for future phases:
/// - Resolution workflow history (Phase 4+)
/// - AI-generated insights (Phase 5+)
/// - Incident links (Phase 6+)
/// - Integration with external systems (Phase 7+)
/// </summary>
public sealed record FailureKnowledge(
    string? RootCause,
    string? ResolutionNotes,
    string? OperationalNotes,
    string? RunbookLink,
    string? Owner,
    string? ReplayGuidance,
    DateTimeOffset? LastUpdatedAt,
    int KnowledgeVersion,
    DateTimeOffset? ReviewDueAt,
    string? Tags,
    string? UpdatedBy = null)
{
    /// <summary>
    /// Friendly summary of this knowledge for logging and UI display.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(RootCause))
            parts.Add($"Cause: {RootCause}");
        if (!string.IsNullOrWhiteSpace(Owner))
            parts.Add($"Owner: {Owner}");
        if (!string.IsNullOrWhiteSpace(ReplayGuidance))
            parts.Add($"Replay: {ReplayGuidance}");

        return parts.Count > 0
            ? string.Join(" | ", parts)
            : "No knowledge recorded";
    }
}
