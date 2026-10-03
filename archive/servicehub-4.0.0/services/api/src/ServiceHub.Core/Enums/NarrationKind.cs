namespace ServiceHub.Core.Enums;

/// <summary>
/// The shape of emergent pattern a <see cref="Entities.Narration"/> summarizes
/// (roadmap §5.B, I4 — "Narrate").
/// </summary>
public enum NarrationKind
{
    /// <summary>Anomalies and/or drift findings observed within a single namespace.</summary>
    NamespaceActivity = 0,

    /// <summary>A same-provider correlation spanning two or more namespaces (roadmap §5.D, C1).</summary>
    CrossNamespaceCorrelation = 1,
}
