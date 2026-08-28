using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents a detected message-shape drift for an entity — a leading indicator that a
/// producer shipped a breaking change, surfaced before volume becomes a full incident
/// (roadmap §5.C, P1/P2).
/// </summary>
public sealed class DriftFinding
{
    /// <summary>
    /// Gets the unique identifier for this drift finding.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the namespace ID where the drift was detected.
    /// </summary>
    public Guid NamespaceId { get; private set; }

    /// <summary>
    /// Gets the entity name (queue, topic, or subscription) associated with the finding.
    /// </summary>
    public string EntityName { get; private set; }

    /// <summary>
    /// Gets the type of drift detected.
    /// </summary>
    public DriftFindingType Type { get; private set; }

    /// <summary>
    /// Gets the severity level (0-100, where 100 is most severe).
    /// </summary>
    public int Severity { get; private set; }

    /// <summary>
    /// Gets the description of the drift finding.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// Gets the timestamp when the drift was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>
    /// Gets the metrics associated with this finding.
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics { get; private set; }

    /// <summary>
    /// Gets the recommended actions to address this finding.
    /// </summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    /// <summary>
    /// Private constructor for entity creation.
    /// </summary>
    private DriftFinding()
    {
        EntityName = string.Empty;
        Description = string.Empty;
        Metrics = new Dictionary<string, double>();
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new drift finding instance.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="entityName">The entity name.</param>
    /// <param name="type">The drift type.</param>
    /// <param name="severity">The severity level (0-100).</param>
    /// <param name="description">The finding description.</param>
    /// <param name="metrics">Associated metrics.</param>
    /// <param name="recommendedActions">Recommended actions.</param>
    /// <returns>A new drift finding instance.</returns>
    public static DriftFinding Create(
        Guid namespaceId,
        string entityName,
        DriftFindingType type,
        int severity,
        string description,
        IReadOnlyDictionary<string, double>? metrics = null,
        IReadOnlyList<string>? recommendedActions = null)
    {
        return new DriftFinding
        {
            Id = Guid.NewGuid(),
            NamespaceId = namespaceId,
            EntityName = entityName ?? throw new ArgumentNullException(nameof(entityName)),
            Type = type,
            Severity = Math.Clamp(severity, 0, 100),
            Description = description ?? throw new ArgumentNullException(nameof(description)),
            DetectedAt = DateTimeOffset.UtcNow,
            Metrics = metrics ?? new Dictionary<string, double>(),
            RecommendedActions = recommendedActions ?? Array.Empty<string>()
        };
    }
}
