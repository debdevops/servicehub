using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.InsightDetected"/>. Carries whichever detection pillar
/// (<see cref="InsightKind"/>) produced the finding.
/// </summary>
public sealed record InsightDetectedPayload
{
    /// <summary>Which detection pillar produced this finding.</summary>
    public required InsightKind Kind { get; init; }

    /// <summary>The finding's own identifier (<c>Anomaly.Id</c>, <c>DriftFinding.Id</c>,
    /// <c>CorrelationFinding.Id</c>, or <c>Narration.Id</c>).</summary>
    public required Guid FindingId { get; init; }

    /// <summary>The queue/topic/subscription this finding is about, when it concerns a single
    /// entity. Null for cross-namespace correlation and narration findings.</summary>
    public string? EntityName { get; init; }

    /// <summary>Human-readable description of the finding.</summary>
    public required string Description { get; init; }

    /// <summary>The finding's severity (0-100, where 100 is most severe).</summary>
    public required int Severity { get; init; }

    /// <summary>UTC timestamp when the finding was detected/generated.</summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }
}
