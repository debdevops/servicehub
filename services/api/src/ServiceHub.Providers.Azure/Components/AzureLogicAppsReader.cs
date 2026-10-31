using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>Reads Logic Apps through Azure Resource Manager. <c>GET</c> only, by construction: <see cref="IAzureRestClient"/> has no other verb.</summary>
internal sealed class AzureLogicAppsReader : IAzureLogicAppsReader
{
    private const string ApiVersion = "2016-06-01";

    private readonly IAzureRestClient _rest;

    public AzureLogicAppsReader(IAzureRestClient rest) => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

    public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) => AzureArm.ListSubscriptionsAsync(_rest, ns, ct);

    public async Task<Listing<LogicAppRef>> ListWorkflowsAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct)
    {
        var url = AzureArm.Url($"/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.Logic/workflows", ApiVersion, ("$top", max.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var doc = AzureArm.Parse(await _rest.GetTextAsync(ns, url, AzureAudience.Management, null, ct).ConfigureAwait(false));
        var items = AzureArm.Values(doc).Select(v => AzureArm.Text(v, "id")).Where(id => id is not null).Select(id => new LogicAppRef(id!)).ToList();
        return new Listing<LogicAppRef>([.. items.Take(max)], items.Count > max || AzureArm.HasNext(doc));
    }

    public async Task<Listing<LogicRunRef>> ListRunsAsync(Namespace ns, string workflowId, string status, int max, CancellationToken ct)
    {
        var url = AzureArm.Url($"{workflowId}/runs", ApiVersion, ("$filter", $"status eq '{status}'"), ("$top", max.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var doc = AzureArm.Parse(await _rest.GetTextAsync(ns, url, AzureAudience.Management, null, ct).ConfigureAwait(false));
        var items = AzureArm.Values(doc).Select(ToRun).Where(r => r is not null).Select(r => r!).ToList();
        return new Listing<LogicRunRef>([.. items.Take(max)], items.Count > max || AzureArm.HasNext(doc));
    }

    public async Task<LogicRunRead?> GetRunAsync(Namespace ns, string runId, CancellationToken ct)
    {
        var text = await _rest.GetTextAsync(ns, AzureArm.Url(runId, ApiVersion), AzureAudience.Management, null, ct).ConfigureAwait(false);
        if (text is null)
        {
            return null;
        }

        using var doc = AzureArm.Parse(text);
        var run = ToRun(doc.RootElement);
        if (run is null)
        {
            return null;
        }

        // Only the error and the trigger's name are read: a run's trigger and action inputs and outputs can hold customer data, and are never touched.
        var props = AzureArm.Child(doc.RootElement, "properties");
        var error = AzureArm.Child(props, "error");
        return new LogicRunRead(run, AzureArm.Text(error, "code") ?? AzureArm.Text(props, "code"), AzureArm.Text(error, "message"), AzureArm.Text(AzureArm.Child(props, "trigger"), "name"));
    }

    private static LogicRunRef? ToRun(System.Text.Json.JsonElement element)
    {
        var id = AzureArm.Text(element, "id");
        var props = AzureArm.Child(element, "properties");
        return id is null ? null : new LogicRunRef(id, AzureArm.Text(props, "status") ?? "Unknown", AzureArm.Time(props, "startTime"), AzureArm.Time(props, "endTime"));
    }
}
