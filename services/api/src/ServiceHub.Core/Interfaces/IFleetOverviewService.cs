using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Cross-namespace ("fleet") operations view. Where <see cref="IDlqHistoryService"/> answers
/// "what is happening in <i>this</i> namespace?", this answers the operations-platform question
/// "what died overnight across <i>everything</i> I own?" — the daily-glance that turns ServiceHub
/// from a break-glass tool into an operations dashboard.
/// </summary>
public interface IFleetOverviewService
{
    /// <summary>
    /// Builds a fleet-wide DLQ overview for the given owner.
    /// </summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="windowHours">The "recent activity" window, in hours (clamped 1–720). Drives
    /// the "new in window" counts that surface what changed overnight.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<FleetOverview>> GetOverviewAsync(
        string ownerId,
        int windowHours = 24,
        CancellationToken cancellationToken = default);
}

/// <summary>Fleet-wide DLQ operations snapshot across all of an owner's namespaces.</summary>
public sealed record FleetOverview(
    DateTimeOffset GeneratedAt,
    int WindowHours,
    int NamespaceCount,
    int TotalActive,
    int TotalNewInWindow,
    int TotalResolvedInWindow,
    IReadOnlyList<FleetNamespaceHealth> Namespaces,
    IReadOnlyDictionary<string, int> TopCategories,
    IReadOnlyList<FleetTrendPoint> DailyTrend);

/// <summary>Per-namespace health line in the fleet overview, sorted worst-first.</summary>
public sealed record FleetNamespaceHealth(
    Guid NamespaceId,
    string NamespaceName,
    string Provider,
    string Environment,
    int ActiveCount,
    int NewInWindow,
    int ResolvedInWindow,
    int TotalCount,
    string? TopEntity,
    int TopEntityCount,
    string? TopCategory,
    DateTimeOffset? OldestActiveDetectedAt,
    FleetHealthSeverity Severity);

/// <summary>A fleet-wide daily trend point (new vs resolved across all namespaces).</summary>
public sealed record FleetTrendPoint(
    DateTimeOffset Date,
    int NewMessages,
    int ResolvedMessages);

/// <summary>Coarse health signal for a namespace, derived from DLQ activity.</summary>
public enum FleetHealthSeverity
{
    /// <summary>No active dead-letters.</summary>
    Healthy = 0,

    /// <summary>Active dead-letters present, but below the critical threshold.</summary>
    Warning = 1,

    /// <summary>A spike in the window or a large active backlog needs attention.</summary>
    Critical = 2
}
