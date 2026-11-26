using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>A page, whether more follow it, and whether part of the read failed so the page may be missing items.</summary>
internal sealed record Listing<T>(IReadOnlyList<T> Items, bool More, bool Incomplete = false);

/// <summary>A Logic App (workflow), by its full resource id.</summary>
internal sealed record LogicAppRef(string Id);

/// <summary>A workflow run, by its full resource id, with the status Azure reports.</summary>
internal sealed record LogicRunRef(string Id, string Status, DateTimeOffset? StartedUtc, DateTimeOffset? EndedUtc);

/// <summary>
/// One run read in full. Never carries the run's trigger or action inputs and outputs. <paramref name="TriggerName"/> is only the
/// trigger's name, which a resubmit needs to build its address.
/// </summary>
internal sealed record LogicRunRead(LogicRunRef Run, string? ErrorCode, string? ErrorMessage, string? TriggerName = null);

/// <summary>
/// The only way <see cref="LogicAppsSource"/> touches Azure: list subscriptions, list Logic Apps, list runs, get one run. Narrow on
/// purpose, so a test can stand in for Azure and the read-only promise is one small surface to read.
/// </summary>
internal interface IAzureLogicAppsReader
{
    /// <summary>The subscription ids the connection's identity can see.</summary>
    Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct);

    /// <summary>Logic Apps in one subscription.</summary>
    Task<Listing<LogicAppRef>> ListWorkflowsAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct);

    /// <summary>Runs of one Logic App that ended in <paramref name="status"/>.</summary>
    Task<Listing<LogicRunRef>> ListRunsAsync(Namespace ns, string workflowId, string status, int max, CancellationToken ct);

    /// <summary>One run, or null when Azure no longer has it.</summary>
    Task<LogicRunRead?> GetRunAsync(Namespace ns, string runId, CancellationToken ct);

    /// <summary>
    /// The state Azure reports for one Logic App (<c>Enabled</c>, <c>Disabled</c>, <c>Suspended</c>...), or null when Azure no longer has it.
    /// A resubmit needs it: Azure runs a resubmit on a disabled Logic App anyway.
    /// </summary>
    Task<string?> GetWorkflowStateAsync(Namespace ns, string workflowId, CancellationToken ct);
}
