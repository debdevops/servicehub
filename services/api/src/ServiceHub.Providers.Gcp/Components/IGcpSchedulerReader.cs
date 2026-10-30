using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// A Cloud Scheduler job as Google describes it, without its target. <see cref="Name"/> is the full resource name
/// (<c>projects/p/locations/l/jobs/j</c>).
/// </summary>
/// <param name="Name">The job's resource name.</param>
/// <param name="State">Google's own word: <c>ENABLED</c>, <c>PAUSED</c>, <c>DISABLED</c> or <c>UPDATE_FAILED</c>.</param>
/// <param name="LastAttemptUtc">When the job last ran, or null when it has not run yet.</param>
/// <param name="StatusCode">The gRPC status code of the last attempt (0 is OK), or null when there has been no attempt.</param>
/// <param name="StatusMessage">What Google recorded for that attempt, or null.</param>
internal sealed record SchedulerJobRef(string Name, string State, DateTimeOffset? LastAttemptUtc, int? StatusCode, string? StatusMessage);

/// <summary>
/// The only way <see cref="CloudSchedulerSource"/> touches Google: list locations, list jobs, get one job. Narrow on purpose, so a test
/// can stand in for Google and the read-only promise is one small surface to read. A job's target (URL, headers, body) is never mapped.
/// </summary>
internal interface IGcpSchedulerReader
{
    /// <summary>The location ids Cloud Scheduler is offered in for the connection's project.</summary>
    Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct);

    /// <summary>Jobs in one location (or in every location when <paramref name="location"/> is <c>-</c>).</summary>
    Task<Listing<SchedulerJobRef>> ListJobsAsync(Namespace ns, string location, int max, CancellationToken ct);

    /// <summary>One job, or null when Google no longer has it.</summary>
    Task<SchedulerJobRef?> GetJobAsync(Namespace ns, string jobName, CancellationToken ct);
}
