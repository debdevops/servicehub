using System.Text.RegularExpressions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Google Cloud Workflows failed and cancelled executions (build-plan unit PP3, the second source). <b>Read-only</b>: it lists locations,
/// workflows and executions, and reads one execution in the BASIC view, so an execution's argument and result are never read.
/// </summary>
/// <remarks>
/// A project's workflows are spread over locations and the connection stores no location, so the places are found by asking each
/// location Workflows is offered in (a few at a time, inside one time budget). The list does not carry an execution's error, so
/// <see cref="ComponentSourceCapabilities.ReasonInList"/> is false. Needs <c>workflows.workflows.list</c>, <c>workflows.executions.list</c>
/// and <c>workflows.executions.get</c> (the Workflows Viewer role) and the Workflow Executions API turned on.
/// </remarks>
public sealed partial class WorkflowsSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "workflows";

    /// <summary>Without a chosen place, at most this many workflows are read, so one listing stays a bounded number of calls.</summary>
    internal const int MaxWorkflowsPerListing = 20;

    // Google's own words for how an execution ended badly. A cancelled run is shown as it is, never relabelled "aborted".
    private static readonly string[] States = ["FAILED", "CANCELLED"];

    private const int MaxTextLength = 1000;

    private readonly IGcpWorkflowsReader _reader;

    // The reader is this module's own seam to Google, so a test can stand in for it; only the public constructor is for the host.
    internal WorkflowsSource(IGcpWorkflowsReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Google.</summary>
    public WorkflowsSource(IGcpClientFactory clients)
        : this(new GcpWorkflowsReader(clients))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Gcp;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: false, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure(NoProject());
        }

        try
        {
            // Listing is permitted per project, not per location, so one location answers for all of them.
            await _reader.ListWorkflowsAsync(ns, "us-central1", 1, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cloud failure becomes a result the screen can show, never an exception.
        catch (Exception ex)
        {
            return Result.Failure(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<SourcePlacesPage>(NoProject());
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var found = await FindWorkflowsAsync(ns, ComponentSourceLimits.MaxPlaces, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<SourcePlacesPage>(found.Error);
            }

            return Result.Success(new SourcePlacesPage(
                [.. found.Items.Take(ComponentSourceLimits.MaxPlaces).Select(w => new SourcePlace(w.Name, ShortName(w.Name), "workflow", WorkflowLink(w.Name)))],
                found.More || found.Items.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("GCP.Workflows.ReadTimedOut", "Reading Workflows took too long."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<SourcePlacesPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemsPage>(NoProject());
        }

        var place = placeId is null ? null : InOwnProject(ns, placeId, execution: false);
        if (placeId is not null && place is null)
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("GCP.Workflows.InvalidPlace", "That is not a workflow in this connection's project."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var reached = false;
            var incomplete = false;
            List<string> workflows;
            if (placeId is null)
            {
                var found = await FindWorkflowsAsync(ns, MaxWorkflowsPerListing + 1, budget.Token, ct).ConfigureAwait(false);
                if (found.Error is not null)
                {
                    return Result.Failure<FailedItemsPage>(found.Error);
                }

                workflows = [.. found.Items.Take(MaxWorkflowsPerListing).Select(w => w.Name)];
                reached = found.More || found.Items.Count > MaxWorkflowsPerListing;
                incomplete = found.Incomplete;
            }
            else
            {
                workflows = [place!];
            }

            var reads = workflows.SelectMany(w => States.Select(state => (Workflow: w, State: state))).ToList();
            var results = await SourceReads.RunAsync(
                reads, (r, token) => _reader.ListExecutionsAsync(ns, r.Workflow, r.State, ComponentSourceLimits.MaxFailedItems, token),
                Fail, new Listing<ExecutionRef>([], false), budget.Token, ct).ConfigureAwait(false);

            incomplete |= results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Workflows.ReadFailed", "Workflows could not be read."));
            }

            reached |= results.Any(r => r.Value.More);
            var ordered = results
                .SelectMany(r => r.Value.Items)
                .OrderByDescending(e => e.EndedUtc ?? e.StartedUtc ?? DateTimeOffset.MinValue)
                .ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems).Select(e => ToItem(ns, e))], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("GCP.Workflows.ReadTimedOut", "Reading Workflows took too long. Try one workflow."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<FailedItemsPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemDetail?>(NoProject());
        }

        var own = InOwnProject(ns, itemId, execution: true);
        if (own is null)
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("GCP.Workflows.InvalidItem", "That is not an execution in this connection's project."));
        }

        try
        {
            var read = await _reader.GetExecutionAsync(ns, own, ct).ConfigureAwait(false);
            if (read is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            // The error text is what the workflow itself raised: free text, redacted and bounded before it leaves this source.
            var item = ToItem(ns, read.Execution);
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(item, Clean(read.ErrorPayload), Clean(read.ErrorContext), read.Execution.StartedUtc, read.Execution.EndedUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<FailedItemDetail?>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    private sealed record Found(IReadOnlyList<WorkflowRef> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every workflow in every location Workflows is offered in, sorted by name. A location that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindWorkflowsAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        // "-" in place of a location lists every location in one call (the same as Eventarc). Walking Workflows' locations 4 at a time
        // used most of the 20 s budget on the first live run. The walk stays as the fallback if Google refuses the wildcard.
#pragma warning disable CA1031
        try
        {
            var wide = await _reader.ListWorkflowsAsync(ns, "-", max, budget).ConfigureAwait(false);
            return new Found([.. wide.Items.OrderBy(w => w.Name, StringComparer.Ordinal)], wide.More, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fall through to the per-location walk
        }
#pragma warning restore CA1031

        var locations = await _reader.ListLocationsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            locations.Items, (location, token) => _reader.ListWorkflowsAsync(ns, location, max, token), Fail, new Listing<WorkflowRef>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Workflows.ReadFailed", "Workflows could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items).OrderBy(w => w.Name, StringComparer.Ordinal).ToList();
        return new Found(all, locations.More || results.Any(r => r.Value.More), results.Any(r => r.Failed), null);
    }

    private static FailedItem ToItem(Namespace ns, ExecutionRef e) =>
        new(e.Name, WorkflowOf(e.Name), e.EndedUtc ?? e.StartedUtc, e.State, ReasonCode: null, $"{Words(e.State)}: {Clean(ShortName(e.Name))}", CopyKind.None, ExecutionLink(ns, e.Name));

    private static string Words(string state) => state switch
    {
        "FAILED" => "Failed",
        "CANCELLED" => "Cancelled",
        _ => state,
    };

    private static string? ProjectOf(Namespace ns) => string.IsNullOrWhiteSpace(ns.GcpProjectId) ? null : ns.GcpProjectId.Trim();

    private static Error NoProject() =>
        Error.Validation("GCP.Workflows.NoProject", "This connection has no Google project id, so Workflows cannot be read.");

    // projects/{project}/locations/{location}/workflows/{workflow}[/executions/{execution}], in this connection's own project only.
    [GeneratedRegex(@"^projects/(?<p>[^/]+)/locations/(?<l>[^/]+)/workflows/(?<w>[^/]+)(?:/executions/(?<e>[^/]+))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NameRegex();

    // Google lists workflows as projects/{project id}/... but names an execution by the project NUMBER (projects/1078147762303/...), while a
    // connection stores the id. A name is accepted when its project segment is this connection's id or any number, and it is then REWRITTEN
    // to the connection's own project id before anything is read, so the read can only ever happen in this connection's project. (A number
    // that belongs to another project reads that name in OUR project, finds nothing, and never touches the other one.) Any other id is refused.
    private static string? InOwnProject(Namespace ns, string name, bool execution)
    {
        var m = name.Length <= 1024 ? NameRegex().Match(name) : Match.Empty;
        if (!m.Success || m.Groups["e"].Success != execution)
        {
            return null;
        }

        var segment = m.Groups["p"].Value;
        var project = ProjectOf(ns);
        if (project is null)
        {
            return null;
        }

        if (string.Equals(segment, project, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return segment.Length > 0 && segment.All(char.IsAsciiDigit) ? $"projects/{project}{name[(name.IndexOf('/', "projects/".Length))..]}" : null;
    }

    private static string ShortName(string name) => name[(name.LastIndexOf('/') + 1)..];

    private static string WorkflowOf(string executionName)
    {
        var at = executionName.IndexOf("/executions/", StringComparison.Ordinal);
        return at < 0 ? executionName : executionName[..at];
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = LogRedactor.SanitiseForLog(LogRedactor.Redact(text));
        return safe.Length <= MaxTextLength ? safe : safe[..MaxTextLength] + "…";
    }

    private static string WorkflowLink(string workflowName)
    {
        var m = NameRegex().Match(workflowName);
        return $"https://console.cloud.google.com/workflows/workflow/{m.Groups["l"].Value}/{m.Groups["w"].Value}/executions?project={m.Groups["p"].Value}";
    }

    private static string ExecutionLink(Namespace ns, string executionName)
    {
        var m = NameRegex().Match(executionName);
        return m.Success
            ? $"https://console.cloud.google.com/workflows/workflow/{m.Groups["l"].Value}/{m.Groups["w"].Value}/execution/{m.Groups["e"].Value}?project={ProjectOf(ns)}"
            : "https://console.cloud.google.com/workflows";
    }

    private static Error Fail(Exception ex) => ex is RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.Unauthenticated }
        ? Error.Forbidden("GCP.Workflows.NotEnabled",
            "Not enabled for this connection: its service account may not read Workflows (needs workflows.workflows.list, workflows.executions.list and workflows.executions.get, " +
            "the Workflows Viewer role, and the Workflow Executions API turned on).")
        : Error.ExternalService("GCP.Workflows.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
