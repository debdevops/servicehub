namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the kind of message-shape drift detected against an entity's rolling baseline
/// (roadmap §5.C, P1/P2 — "Baseline the good" / "Drift detection").
/// </summary>
public enum DriftFindingType
{
    /// <summary>
    /// No drift detected.
    /// </summary>
    None = 0,

    /// <summary>
    /// A meaningful share of recent messages carry a top-level field shape (schema fingerprint)
    /// not part of the entity's established baseline — e.g. a field was added, renamed, or removed.
    /// </summary>
    SchemaShapeDrift = 1,

    /// <summary>
    /// The entity's dominant payload format itself changed (e.g. JSON object to plain text or
    /// binary) — a stronger breaking-change signal than a field-level shape drift.
    /// </summary>
    PayloadFormatDrift = 2,
}
