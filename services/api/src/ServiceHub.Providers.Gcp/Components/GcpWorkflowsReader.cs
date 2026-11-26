using Google.Cloud.Location;
using Google.Cloud.Workflows.Executions.V1;
using Google.Cloud.Workflows.V1;
using Grpc.Core;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Google's side of <see cref="IGcpWorkflowsReader"/>. <b>Read-only</b>: it calls only <c>ListLocations</c>, <c>ListWorkflows</c>,
/// <c>ListExecutions</c> and <c>GetExecution</c>, and asks for the BASIC view, which leaves out an execution's argument and result.
/// </summary>
internal sealed class GcpWorkflowsReader : IGcpWorkflowsReader
{
    private readonly IGcpClientFactory _clients;

    public GcpWorkflowsReader(IGcpClientFactory clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public async Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct)
    {
        var client = await _clients.GetWorkflowsClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client.LocationsClient
            .ListLocationsAsync(new ListLocationsRequest { Name = $"projects/{ProjectOf(ns)}" })
            .ReadPageAsync(100, ct).ConfigureAwait(false);
        return new Listing<string>([.. page.Select(l => l.LocationId)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<Listing<WorkflowRef>> ListWorkflowsAsync(Namespace ns, string location, int max, CancellationToken ct)
    {
        var client = await _clients.GetWorkflowsClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client
            .ListWorkflowsAsync(new ListWorkflowsRequest { Parent = $"projects/{ProjectOf(ns)}/locations/{location}", PageSize = max })
            .ReadPageAsync(max, ct).ConfigureAwait(false);
        return new Listing<WorkflowRef>([.. page.Select(w => new WorkflowRef(w.Name))], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<Listing<ExecutionRef>> ListExecutionsAsync(Namespace ns, string workflowName, string state, int max, CancellationToken ct)
    {
        var client = await _clients.GetExecutionsClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client
            .ListExecutionsAsync(new ListExecutionsRequest { Parent = workflowName, Filter = $"state=\"{state}\"", PageSize = max, View = ExecutionView.Basic })
            .ReadPageAsync(max, ct).ConfigureAwait(false);
        return new Listing<ExecutionRef>([.. page.Select(Map)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<ExecutionRead?> GetExecutionAsync(Namespace ns, string executionName, CancellationToken ct)
    {
        var client = await _clients.GetExecutionsClientAsync(ns, ct).ConfigureAwait(false);
        try
        {
            var e = await client.GetExecutionAsync(new GetExecutionRequest { Name = executionName, View = ExecutionView.Basic }, ct).ConfigureAwait(false);
            return new ExecutionRead(Map(e), e.Error?.Payload, e.Error?.Context);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private static ExecutionRef Map(Execution e) =>
        new(e.Name, e.State.ToString().ToUpperInvariant(), e.StartTime?.ToDateTimeOffset(), e.EndTime?.ToDateTimeOffset());

    private static string ProjectOf(Namespace ns) =>
        string.IsNullOrWhiteSpace(ns.GcpProjectId) ? throw new InvalidOperationException("This connection has no Google project id.") : ns.GcpProjectId.Trim();
}
