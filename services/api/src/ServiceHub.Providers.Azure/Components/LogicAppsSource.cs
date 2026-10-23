using System.Text.RegularExpressions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>
/// Azure Logic Apps failed, timed-out and cancelled runs (build-plan unit PP4d). <b>Read-only</b>: it lists subscriptions, Logic Apps
/// and runs, and reads one run's error, and never a run's trigger or action inputs and outputs.
/// </summary>
/// <remarks>
/// A connection stores no subscription, so the places are found by asking every subscription the connection's Entra identity can see (a
/// few at a time, inside one time budget). The list does not carry a run's error, so <see cref="ComponentSourceCapabilities.ReasonInList"/>
/// is false. Needs the Reader role (<c>Microsoft.Logic/workflows/read</c>, <c>Microsoft.Logic/workflows/runs/read</c>).
/// </remarks>
public sealed partial class LogicAppsSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "logic-apps";

    /// <summary>Without a chosen place, at most this many Logic Apps are read, so one listing stays a bounded number of calls.</summary>
    internal const int MaxWorkflowsPerListing = 20;

    // Azure's own words for how a run ended badly. A cancelled run is shown as it is, never relabelled "failed".
    private static readonly string[] States = ["Failed", "TimedOut", "Cancelled"];

    private const int MaxTextLength = 1000;
    private const string Needs = "the Reader role on the subscription";

    private readonly IAzureLogicAppsReader _reader;

    // The reader is this module's own seam to Azure, so a test can stand in for it; only the public constructor is for the host.
    internal LogicAppsSource(IAzureLogicAppsReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Azure Resource Manager.</summary>
    public LogicAppsSource(IAzureRestClient rest)
        : this(new AzureLogicAppsReader(rest))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Azure;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: false, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var subscriptions = await _reader.ListSubscriptionsAsync(ns, ct).ConfigureAwait(false);
            if (subscriptions.Items.Count == 0)
            {
                return Result.Failure(Error.Forbidden("Azure.LogicApps.NotEnabled", "Not enabled for this connection: its identity cannot see any Azure subscription. Give it the Reader role on the subscription that holds the Logic Apps."));
            }

            // Listing the subscriptions needs no role on a Logic App, so prove the Logic Apps themselves can be listed.
            await _reader.ListWorkflowsAsync(ns, subscriptions.Items[0], 1, ct).ConfigureAwait(false);
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
                [.. found.Items.Take(ComponentSourceLimits.MaxPlaces).Select(w => new SourcePlace(w.Id, ShortName(w.Id), "logic app", PortalLink(w.Id)))],
                found.More || found.Items.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("Azure.LogicApps.ReadTimedOut", "Reading Logic Apps took too long."));
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
        if (placeId is not null && !IsWorkflowId(placeId))
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("Azure.LogicApps.InvalidPlace", "That is not a Logic App."));
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

                workflows = [.. found.Items.Take(MaxWorkflowsPerListing).Select(w => w.Id)];
                reached = found.More || found.Items.Count > MaxWorkflowsPerListing;
                incomplete = found.Incomplete;
            }
            else
            {
                workflows = [placeId];
            }

            var reads = workflows.SelectMany(w => States.Select(state => (Workflow: w, State: state))).ToList();
            var results = await SourceReads.RunAsync(
                reads, (r, token) => _reader.ListRunsAsync(ns, r.Workflow, r.State, ComponentSourceLimits.MaxFailedItems, token),
                Fail, new Listing<LogicRunRef>([], false), budget.Token, ct).ConfigureAwait(false);

            incomplete |= results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.LogicApps.ReadFailed", "Logic Apps could not be read."));
            }

            reached |= results.Any(r => r.Value.More);
            var ordered = results
                .SelectMany(r => r.Value.Items)
                .OrderByDescending(e => e.EndedUtc ?? e.StartedUtc ?? DateTimeOffset.MinValue)
                .ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems).Select(ToItem)], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("Azure.LogicApps.ReadTimedOut", "Reading Logic Apps took too long. Try one Logic App."));
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
        if (!IsRunId(itemId))
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("Azure.LogicApps.InvalidItem", "That is not a Logic App run."));
        }

        try
        {
            var read = await _reader.GetRunAsync(ns, itemId, ct).ConfigureAwait(false);
            if (read is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            // The error text is what the workflow itself raised: free text, redacted and bounded before it leaves this source.
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(ToItem(read.Run), Clean(read.ErrorCode), Clean(read.ErrorMessage), read.Run.StartedUtc, read.Run.EndedUtc));
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

    private sealed record Found(IReadOnlyList<LogicAppRef> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every Logic App in every subscription the identity can see, sorted by id. A subscription that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindWorkflowsAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        var subscriptions = await _reader.ListSubscriptionsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            subscriptions.Items, (sub, token) => _reader.ListWorkflowsAsync(ns, sub, max, token), Fail, new Listing<LogicAppRef>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.LogicApps.ReadFailed", "Logic Apps could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items).OrderBy(w => w.Id, StringComparer.OrdinalIgnoreCase).ToList();
        return new Found(all, subscriptions.More || results.Any(r => r.Value.More), results.Any(r => r.Failed), null);
    }

    private static FailedItem ToItem(LogicRunRef run) =>
        new(run.Id, WorkflowOf(run.Id), run.EndedUtc ?? run.StartedUtc, run.Status, ReasonCode: null, $"{Words(run.Status)}: {Clean(ShortName(run.Id))}", CopyKind.None, PortalLink(WorkflowOf(run.Id)));

    private static string Words(string status) => status switch
    {
        "Failed" => "Failed",
        "TimedOut" => "Timed out",
        "Cancelled" => "Cancelled",
        _ => status,
    };

    // /subscriptions/{id}/resourceGroups/{group}/providers/Microsoft.Logic/workflows/{name}[/runs/{run}]
    [GeneratedRegex(@"^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/?#\s]{1,90}/providers/Microsoft\.Logic/workflows/[^/?#\s]{1,80}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WorkflowIdRegex();

    [GeneratedRegex(@"^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/?#\s]{1,90}/providers/Microsoft\.Logic/workflows/[^/?#\s]{1,80}/runs/[^/?#\s]{1,80}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RunIdRegex();

    private static bool IsWorkflowId(string id) => id.Length <= 512 && WorkflowIdRegex().IsMatch(id);

    private static bool IsRunId(string id) => id.Length <= 600 && RunIdRegex().IsMatch(id);

    private static string ShortName(string id) => id[(id.LastIndexOf('/') + 1)..];

    private static string WorkflowOf(string runId)
    {
        var at = runId.IndexOf("/runs/", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? runId : runId[..at];
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

    private static string PortalLink(string workflowId) => $"https://portal.azure.com/#@/resource{workflowId}/logicApp";

    private static Error Fail(Exception ex) => AzureFailures.ToError(ex, "Azure.LogicApps", Needs);
}
