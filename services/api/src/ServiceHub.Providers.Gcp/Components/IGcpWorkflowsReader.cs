using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>A workflow, by its full resource name (<c>projects/p/locations/l/workflows/w</c>).</summary>
internal sealed record WorkflowRef(string Name);

/// <summary>A workflow execution, by its full resource name, with the state Google reports.</summary>
internal sealed record ExecutionRef(string Name, string State, DateTimeOffset? StartedUtc, DateTimeOffset? EndedUtc);

/// <summary>One execution read in full. Never carries the execution's argument or result.</summary>
internal sealed record ExecutionRead(ExecutionRef Execution, string? ErrorPayload, string? ErrorContext);

/// <summary>A page and whether more follow it.</summary>
internal sealed record Listing<T>(IReadOnlyList<T> Items, bool More);

/// <summary>
/// The only way <see cref="WorkflowsSource"/> touches Google: list locations, list workflows, list executions, get one execution. Narrow on
/// purpose, so a test can stand in for Google and so the read-only promise is one small surface to read.
/// </summary>
internal interface IGcpWorkflowsReader
{
    /// <summary>The location ids Workflows is offered in for the connection's project.</summary>
    Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct);

    /// <summary>Workflows in one location.</summary>
    Task<Listing<WorkflowRef>> ListWorkflowsAsync(Namespace ns, string location, int max, CancellationToken ct);

    /// <summary>Executions of one workflow that ended in <paramref name="state"/>, newest first.</summary>
    Task<Listing<ExecutionRef>> ListExecutionsAsync(Namespace ns, string workflowName, string state, int max, CancellationToken ct);

    /// <summary>One execution, or null when Google no longer has it.</summary>
    Task<ExecutionRead?> GetExecutionAsync(Namespace ns, string executionName, CancellationToken ct);
}
