namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Tunables for <see cref="WorkerHeartbeatHealthCheck"/>. Bound from the
/// <c>WorkerHeartbeat</c> configuration section (roadmap §6, cross-cutting foundation item 4).
/// </summary>
public sealed class WorkerHeartbeatHealthCheckOptions
{
    /// <summary>Defaults applied when DI does not supply an instance — e.g. tests constructing
    /// <see cref="WorkerHeartbeatHealthCheck"/> directly.</summary>
    public static readonly WorkerHeartbeatHealthCheckOptions Default = new();

    /// <summary>
    /// How many multiples of a worker's own configured cadence may elapse since its last
    /// heartbeat before the check reports it stale. Deliberately a multiplier of each worker's
    /// own interval rather than one fixed absolute threshold — the workers this check
    /// covers (see <see cref="WorkerHeartbeatHealthCheck"/>'s own <c>ExpectedWorkers</c> list)
    /// have cadences ranging from 10 seconds to an hour, and a single absolute cutoff
    /// would either false-positive on the slowest workers or never catch a stall in the
    /// fastest ones.
    /// </summary>
    public double StalenessMultiplier { get; init; } = 3.0;
}
