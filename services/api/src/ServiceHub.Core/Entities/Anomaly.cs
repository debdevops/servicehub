using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents a detected anomaly in Service Bus traffic or behavior.
/// </summary>
public sealed class Anomaly
{
    /// <summary>
    /// Gets the unique identifier for this anomaly.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the namespace ID where the anomaly was detected.
    /// </summary>
    public Guid NamespaceId { get; private set; }

    /// <summary>
    /// Gets the entity name (queue, topic, or subscription) associated with the anomaly.
    /// </summary>
    public string EntityName { get; private set; }

    /// <summary>
    /// Gets the type of anomaly detected.
    /// </summary>
    public AnomalyType Type { get; private set; }

    /// <summary>
    /// Gets the severity level (0-100, where 100 is most severe).
    /// </summary>
    public int Severity { get; private set; }

    /// <summary>
    /// Gets the description of the anomaly.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// Gets the timestamp when the anomaly was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>
    /// Gets the metrics associated with this anomaly.
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics { get; private set; }

    /// <summary>
    /// Gets the recommended actions to address this anomaly.
    /// </summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    /// <summary>
    /// Private constructor for entity creation.
    /// </summary>
    private Anomaly()
    {
        EntityName = string.Empty;
        Description = string.Empty;
        Metrics = new Dictionary<string, double>();
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new anomaly instance.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="entityName">The entity name.</param>
    /// <param name="type">The anomaly type.</param>
    /// <param name="severity">The severity level (0-100).</param>
    /// <param name="description">The anomaly description.</param>
    /// <param name="metrics">Associated metrics.</param>
    /// <param name="recommendedActions">Recommended actions.</param>
    /// <returns>A new anomaly instance.</returns>
    public static Anomaly Create(
        Guid namespaceId,
        string entityName,
        AnomalyType type,
        int severity,
        string description,
        IReadOnlyDictionary<string, double>? metrics = null,
        IReadOnlyList<string>? recommendedActions = null)
    {
        return new Anomaly
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
