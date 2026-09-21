using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One entity's anomaly, correlated against the external signal that preceded its onset within
/// the detection window — C3's leading hypothesis that a deploy or config change caused the
/// anomaly, never an assertion of causation (same evidence-over-confidence discipline as
/// <see cref="CorrelationFinding"/>).
/// </summary>
public sealed class ExternalSignalCorrelation
{
    /// <summary>Gets the unique identifier for this correlation.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owner ID whose namespace and signal contributed to this correlation.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Gets the namespace the anomalous entity belongs to.</summary>
    public Guid NamespaceId { get; private set; }

    /// <summary>Gets the anomalous queue, topic, or subscription name.</summary>
    public string EntityName { get; private set; }

    /// <summary>Gets the type of anomaly detected.</summary>
    public AnomalyType AnomalyType { get; private set; }

    /// <summary>Gets the anomaly's own severity (0-100).</summary>
    public int AnomalySeverity { get; private set; }

    /// <summary>Gets the cloud provider of the namespace the anomalous entity belongs to.</summary>
    public CloudProviderType Provider { get; private set; }

    /// <summary>Gets the ID of the external signal this anomaly was correlated against.</summary>
    public Guid SignalId { get; private set; }

    /// <summary>Gets the kind of the correlated external signal.</summary>
    public ExternalSignalType SignalType { get; private set; }

    /// <summary>Gets where the correlated signal came from.</summary>
    public string SignalSource { get; private set; }

    /// <summary>Gets when the correlated signal occurred.</summary>
    public DateTimeOffset SignalOccurredAt { get; private set; }

    /// <summary>Gets the gap between the signal and the anomaly's onset.</summary>
    public TimeSpan Gap { get; private set; }

    /// <summary>Gets the description of the correlation.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the timestamp when this correlation was detected.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Gets the recommended actions to address this correlation.</summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    private ExternalSignalCorrelation()
    {
        OwnerId = string.Empty;
        EntityName = string.Empty;
        SignalSource = string.Empty;
        Description = string.Empty;
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new external-signal correlation. <paramref name="gap"/> is derived by the caller
    /// (anomaly onset minus signal <see cref="ExternalSignalEvent.OccurredAt"/>) rather than
    /// recomputed here, so it always reflects exactly the window the detection service checked.
    /// </summary>
    public static ExternalSignalCorrelation Create(
        string ownerId,
        Guid namespaceId,
        string entityName,
        AnomalyType anomalyType,
        int anomalySeverity,
        CloudProviderType provider,
        ExternalSignalEvent signal,
        TimeSpan gap,
        string description,
        IReadOnlyList<string>? recommendedActions = null)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner ID is required.", nameof(ownerId));
        }

        ArgumentNullException.ThrowIfNull(signal);

        return new ExternalSignalCorrelation
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            NamespaceId = namespaceId,
            EntityName = entityName ?? throw new ArgumentNullException(nameof(entityName)),
            AnomalyType = anomalyType,
            AnomalySeverity = Math.Clamp(anomalySeverity, 0, 100),
            Provider = provider,
            SignalId = signal.Id,
            SignalType = signal.SignalType,
            SignalSource = signal.Source,
            SignalOccurredAt = signal.OccurredAt,
            Gap = gap,
            Description = description ?? throw new ArgumentNullException(nameof(description)),
            DetectedAt = DateTimeOffset.UtcNow,
            RecommendedActions = recommendedActions ?? Array.Empty<string>(),
        };
    }
}
