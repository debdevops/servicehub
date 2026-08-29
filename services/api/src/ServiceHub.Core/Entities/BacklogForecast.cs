namespace ServiceHub.Core.Entities;

/// <summary>
/// A deterministic projection of when an entity's active DLQ backlog will cross a configured
/// alert threshold, derived from its recent growth rate (roadmap §5.E, P4 — "Predictive backlog
/// signal"). Arithmetic, not ML: a linear extrapolation of trailing arrival-rate buckets against
/// the entity's current backlog depth.
/// </summary>
public sealed class BacklogForecast
{
    /// <summary>Gets the unique identifier for this forecast.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the namespace ID this forecast was computed for.</summary>
    public Guid NamespaceId { get; private set; }

    /// <summary>Gets the entity name (queue, topic, or subscription) this forecast concerns.</summary>
    public string EntityName { get; private set; }

    /// <summary>Gets the entity's active (unresolved) DLQ message count at forecast time.</summary>
    public int CurrentBacklogCount { get; private set; }

    /// <summary>Gets the extrapolated backlog growth rate, in messages per hour.</summary>
    public double GrowthRatePerHour { get; private set; }

    /// <summary>Gets the alert threshold this forecast projects a breach against.</summary>
    public int AlertThreshold { get; private set; }

    /// <summary>Gets the projected number of hours until the backlog crosses <see cref="AlertThreshold"/>.</summary>
    public double ProjectedHoursToBreach { get; private set; }

    /// <summary>Gets the projected UTC timestamp of the threshold breach.</summary>
    public DateTimeOffset ProjectedBreachAtUtc { get; private set; }

    /// <summary>Gets the severity level (0-100, where 100 is most severe/imminent).</summary>
    public int Severity { get; private set; }

    /// <summary>Gets the human-readable description of the forecast.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the timestamp when this forecast was computed.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Gets the metrics associated with this forecast.</summary>
    public IReadOnlyDictionary<string, double> Metrics { get; private set; }

    /// <summary>Gets the recommended actions to address the projected breach.</summary>
    public IReadOnlyList<string> RecommendedActions { get; private set; }

    private BacklogForecast()
    {
        EntityName = string.Empty;
        Description = string.Empty;
        Metrics = new Dictionary<string, double>();
        RecommendedActions = Array.Empty<string>();
    }

    /// <summary>
    /// Creates a new backlog forecast instance.
    /// </summary>
    public static BacklogForecast Create(
        Guid namespaceId,
        string entityName,
        int currentBacklogCount,
        double growthRatePerHour,
        int alertThreshold,
        double projectedHoursToBreach,
        int severity,
        string description,
        IReadOnlyDictionary<string, double>? metrics = null,
        IReadOnlyList<string>? recommendedActions = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new BacklogForecast
        {
            Id = Guid.NewGuid(),
            NamespaceId = namespaceId,
            EntityName = entityName ?? throw new ArgumentNullException(nameof(entityName)),
            CurrentBacklogCount = currentBacklogCount,
            GrowthRatePerHour = growthRatePerHour,
            AlertThreshold = alertThreshold,
            ProjectedHoursToBreach = projectedHoursToBreach,
            ProjectedBreachAtUtc = now.AddHours(projectedHoursToBreach),
            Severity = Math.Clamp(severity, 0, 100),
            Description = description ?? throw new ArgumentNullException(nameof(description)),
            DetectedAt = now,
            Metrics = metrics ?? new Dictionary<string, double>(),
            RecommendedActions = recommendedActions ?? Array.Empty<string>()
        };
    }
}
