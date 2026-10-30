using Google.Cloud.Location;
using Google.Cloud.Scheduler.V1;
using Grpc.Core;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Google's side of <see cref="IGcpSchedulerReader"/>. <b>Read-only</b>: it calls only <c>ListLocations</c>, <c>ListJobs</c> and
/// <c>GetJob</c>, and copies a job's name, state and last-attempt status. It never maps the job's HTTP, Pub/Sub or App Engine target.
/// </summary>
internal sealed class GcpSchedulerReader : IGcpSchedulerReader
{
    private readonly IGcpClientFactory _clients;

    public GcpSchedulerReader(IGcpClientFactory clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public async Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct)
    {
        var client = await _clients.GetSchedulerClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client.LocationsClient
            .ListLocationsAsync(new ListLocationsRequest { Name = $"projects/{ProjectOf(ns)}" })
            .ReadPageAsync(100, ct).ConfigureAwait(false);
        return new Listing<string>([.. page.Select(l => l.LocationId)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<Listing<SchedulerJobRef>> ListJobsAsync(Namespace ns, string location, int max, CancellationToken ct)
    {
        var client = await _clients.GetSchedulerClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client
            .ListJobsAsync(new ListJobsRequest { Parent = $"projects/{ProjectOf(ns)}/locations/{location}", PageSize = max })
            .ReadPageAsync(max, ct).ConfigureAwait(false);
        return new Listing<SchedulerJobRef>([.. page.Select(Map)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<SchedulerJobRef?> GetJobAsync(Namespace ns, string jobName, CancellationToken ct)
    {
        var client = await _clients.GetSchedulerClientAsync(ns, ct).ConfigureAwait(false);
        try
        {
            return Map(await client.GetJobAsync(new GetJobRequest { Name = jobName }, ct).ConfigureAwait(false));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private static SchedulerJobRef Map(Job j) =>
        new(j.Name, j.State.ToString().ToUpperInvariant(), j.LastAttemptTime?.ToDateTimeOffset(), j.Status?.Code, string.IsNullOrWhiteSpace(j.Status?.Message) ? null : j.Status.Message);

    private static string ProjectOf(Namespace ns) =>
        string.IsNullOrWhiteSpace(ns.GcpProjectId) ? throw new InvalidOperationException("This connection has no Google project id.") : ns.GcpProjectId.Trim();
}
