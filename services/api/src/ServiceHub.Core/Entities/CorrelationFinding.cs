using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One entity's contribution to a <see cref="CorrelationFinding"/>: the namespace/entity pair
/// and the anomaly detected there in the same detection cycle as the other members.
/// </summary>
/// <param name="NamespaceId">The namespace the anomalous entity belongs to.</param>
/// <param name="EntityName">The queue, topic, or subscription name.</param>
/// <param name="AnomalyType">The type of anomaly detected for this entity.</param>
/// <param name="Severity">The anomaly's own severity (0-100).</param>
public sealed record CorrelationMember(
    Guid NamespaceId,
    string EntityName,
    AnomalyType AnomalyType,
    int Severity);

/// <summary>
/// Represents two or more entities whose anomalies were detected in the same window and share a
/// cloud provider — a proactively-surfaced hypothesis that they are one incident with a common
/// downstream cause, rather than N disconnected signatures an operator has to notice are related
/// (roadmap §5.D, C1 — "Same-provider proactive correlation").
/// </summary>
public sealed class CorrelationFinding
{
    /// <summary>Gets the unique identifier for this correlation finding.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owner ID whose namespaces contributed to this finding.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Gets the cloud provider shared by every member of this finding.</summary>
    public CloudProviderType Provider { get; private set; }

    /// <summary>Gets the entities whose simultaneous anomalies make up this correlation.</summary>
    public IReadOnlyList<CorrelationMember> Members { get; private set; }

    /// <summary>Gets the severity level (0-100, where 100 is most severe).</summary>
    public int Severity { get; private set; }

    /// <summary>Gets the description of the correlation.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the timestamp when the correlation was detected.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Gets the metrics associated with this finding.</summary>
    public IReadOnlyDictionary<string, double> Metrics { get; private set; }

    /// <summary>Gets the recommended actions to address this finding.</summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    private CorrelationFinding()
    {
        OwnerId = string.Empty;
        Description = string.Empty;
        Members = Array.Empty<CorrelationMember>();
        Metrics = new Dictionary<string, double>();
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new correlation finding instance.
    /// </summary>
    public static CorrelationFinding Create(
        string ownerId,
        CloudProviderType provider,
        IReadOnlyList<CorrelationMember> members,
        int severity,
        string description,
        IReadOnlyDictionary<string, double>? metrics = null,
        IReadOnlyList<string>? recommendedActions = null)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner ID is required.", nameof(ownerId));
        }

        return new CorrelationFinding
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Provider = provider,
            Members = members ?? throw new ArgumentNullException(nameof(members)),
            Severity = Math.Clamp(severity, 0, 100),
            Description = description ?? throw new ArgumentNullException(nameof(description)),
            DetectedAt = DateTimeOffset.UtcNow,
            Metrics = metrics ?? new Dictionary<string, double>(),
            RecommendedActions = recommendedActions ?? Array.Empty<string>()
        };
    }
}
