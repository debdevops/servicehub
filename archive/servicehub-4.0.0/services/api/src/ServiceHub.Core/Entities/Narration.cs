using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A template-based, plain-English artifact stitching I1–I3's structured findings
/// (classification, trend, anomaly) together with P1/P2 drift and C1 correlation output into one
/// narrative per emergent pattern (roadmap §5.B, I4 — "Narrate"). Deterministic sentence
/// templates over data the other pillars already computed — no ML, no LLM. A reasoning-companion
/// version later is a quality upgrade to this same artifact, never a new capability.
/// </summary>
public sealed class Narration
{
    /// <summary>Gets the unique identifier for this narration.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the kind of emergent pattern this narration summarizes.</summary>
    public NarrationKind Kind { get; private set; }

    /// <summary>
    /// Gets the single namespace this narration is about, when <see cref="Kind"/> is
    /// <see cref="NarrationKind.NamespaceActivity"/>. Null for
    /// <see cref="NarrationKind.CrossNamespaceCorrelation"/>, which spans multiple namespaces
    /// (see <see cref="AccessNamespaceIds"/>).
    /// </summary>
    public Guid? NamespaceId { get; private set; }

    /// <summary>
    /// Gets every namespace whose data contributed to this narration — always at least one entry,
    /// regardless of <see cref="Kind"/>. Callers use this (not <see cref="NamespaceId"/> alone) to
    /// enforce tenant isolation, since a cross-namespace correlation narration has no single owner.
    /// </summary>
    public IReadOnlyList<Guid> AccessNamespaceIds { get; private set; }

    /// <summary>Gets the one-line headline for this narration.</summary>
    public string Headline { get; private set; }

    /// <summary>Gets the full plain-English summary paragraph.</summary>
    public string Summary { get; private set; }

    /// <summary>Gets the severity level (0-100, where 100 is most severe) — the maximum severity
    /// among the findings this narration stitches together.</summary>
    public int Severity { get; private set; }

    /// <summary>Gets the timestamp when this narration was generated.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Gets the IDs of the anomalies this narration is built from.</summary>
    public IReadOnlyList<Guid> ContributingAnomalyIds { get; private set; }

    /// <summary>Gets the IDs of the drift findings this narration is built from.</summary>
    public IReadOnlyList<Guid> ContributingDriftFindingIds { get; private set; }

    /// <summary>Gets the IDs of the correlation findings this narration is built from.</summary>
    public IReadOnlyList<Guid> ContributingCorrelationFindingIds { get; private set; }

    /// <summary>Gets the recommended actions surfaced by this narration.</summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    private Narration()
    {
        AccessNamespaceIds = Array.Empty<Guid>();
        Headline = string.Empty;
        Summary = string.Empty;
        ContributingAnomalyIds = Array.Empty<Guid>();
        ContributingDriftFindingIds = Array.Empty<Guid>();
        ContributingCorrelationFindingIds = Array.Empty<Guid>();
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new narration instance.
    /// </summary>
    public static Narration Create(
        NarrationKind kind,
        Guid? namespaceId,
        IReadOnlyList<Guid> accessNamespaceIds,
        string headline,
        string summary,
        int severity,
        IReadOnlyList<Guid>? contributingAnomalyIds = null,
        IReadOnlyList<Guid>? contributingDriftFindingIds = null,
        IReadOnlyList<Guid>? contributingCorrelationFindingIds = null,
        IReadOnlyList<string>? recommendedActions = null)
    {
        if (accessNamespaceIds is null || accessNamespaceIds.Count == 0)
        {
            throw new ArgumentException("At least one accessible namespace is required.", nameof(accessNamespaceIds));
        }

        return new Narration
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            NamespaceId = namespaceId,
            AccessNamespaceIds = accessNamespaceIds,
            Headline = headline ?? throw new ArgumentNullException(nameof(headline)),
            Summary = summary ?? throw new ArgumentNullException(nameof(summary)),
            Severity = Math.Clamp(severity, 0, 100),
            GeneratedAt = DateTimeOffset.UtcNow,
            ContributingAnomalyIds = contributingAnomalyIds ?? Array.Empty<Guid>(),
            ContributingDriftFindingIds = contributingDriftFindingIds ?? Array.Empty<Guid>(),
            ContributingCorrelationFindingIds = contributingCorrelationFindingIds ?? Array.Empty<Guid>(),
            RecommendedActions = recommendedActions ?? Array.Empty<string>(),
        };
    }
}
