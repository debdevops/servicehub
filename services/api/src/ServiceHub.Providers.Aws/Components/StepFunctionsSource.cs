using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Aws.Components;

/// <summary>
/// Step Functions failed, timed-out and aborted executions (build-plan unit PP2). <b>Read-only</b>: it calls only
/// <c>ListStateMachines</c>, <c>ListExecutions</c> and <c>DescribeExecution</c>, and never an execution's input or output.
/// </summary>
/// <remarks>
/// The list does not carry an execution's error, so <see cref="ComponentSourceCapabilities.ReasonInList"/> is false and the
/// reason is read per item, on demand. Needs the permissions <c>states:ListStateMachines</c>, <c>states:ListExecutions</c>,
/// <c>states:DescribeExecution</c>.
/// </remarks>
public sealed class StepFunctionsSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "step-functions";

    /// <summary>Without a chosen place, at most this many state machines are read, so one listing stays a bounded number of calls.</summary>
    internal const int MaxMachinesPerListing = 20;

    private static readonly string[] Statuses = ["FAILED", "TIMED_OUT", "ABORTED"];

    private readonly IAwsClientFactory _clients;

    /// <summary>Creates the source.</summary>
    public StepFunctionsSource(IAwsClientFactory clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Aws;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: false, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            await _clients.GetStepFunctionsClient(ns).ListStateMachinesAsync(new ListStateMachinesRequest { MaxResults = 1 }, ct).ConfigureAwait(false);
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
    public async Task<Result<IReadOnlyList<SourcePlace>>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var machines = await ListMachinesAsync(ns, ct).ConfigureAwait(false);
            return Result.Success<IReadOnlyList<SourcePlace>>(
                [.. machines.Items.Select(m => new SourcePlace(m.StateMachineArn, m.Name, "state machine", MachineLink(ns, m.StateMachineArn)))]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<SourcePlace>>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var client = _clients.GetStepFunctionsClient(ns);
            var reached = false;
            List<string> arns;
            if (placeId is null)
            {
                var machines = await ListMachinesAsync(ns, ct).ConfigureAwait(false);
                arns = [.. machines.Items.Take(MaxMachinesPerListing).Select(m => m.StateMachineArn)];
                reached = machines.Items.Count > MaxMachinesPerListing || machines.More;
            }
            else
            {
                arns = [placeId];
            }

            var found = new List<FailedItem>();
            foreach (var arn in arns)
            {
                foreach (var status in Statuses)
                {
                    var response = await client.ListExecutionsAsync(
                        new ListExecutionsRequest { StateMachineArn = arn, StatusFilter = status, MaxResults = ComponentSourceLimits.MaxFailedItems }, ct).ConfigureAwait(false);
                    reached |= !string.IsNullOrEmpty(response.NextToken);
                    foreach (var e in response.Executions)
                    {
                        found.Add(new FailedItem(
                            e.ExecutionArn, arn, Utc(e.StopDate), status, ReasonCode: null,
                            $"{Words(status)}: {e.Name}", CopyKind.None, ExecutionLink(ns, e.ExecutionArn)));
                    }
                }
            }

            var ordered = found.OrderByDescending(i => i.AtUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems)], reached));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
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
        try
        {
            var e = await _clients.GetStepFunctionsClient(ns).DescribeExecutionAsync(new DescribeExecutionRequest { ExecutionArn = itemId }, ct).ConfigureAwait(false);
            var status = e.Status?.Value ?? "UNKNOWN";
            var item = new FailedItem(
                e.ExecutionArn, e.StateMachineArn, Utc(e.StopDate), status, Clean(e.Error), $"{Words(status)}: {e.Name}", CopyKind.None, ExecutionLink(ns, e.ExecutionArn));

            // Cause is free text a workflow wrote: it is redacted before it leaves this source. Input and output are never read.
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(item, Clean(e.Error), Clean(e.Cause), Utc(e.StartDate), Utc(e.StopDate)));
        }
        catch (ExecutionDoesNotExistException)
        {
            return Result.Success<FailedItemDetail?>(null);
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

    private async Task<(IReadOnlyList<StateMachineListItem> Items, bool More)> ListMachinesAsync(Namespace ns, CancellationToken ct)
    {
        var response = await _clients.GetStepFunctionsClient(ns)
            .ListStateMachinesAsync(new ListStateMachinesRequest { MaxResults = ComponentSourceLimits.MaxPlaces }, ct).ConfigureAwait(false);
        return (response.StateMachines, !string.IsNullOrEmpty(response.NextToken));
    }

    private static string Words(string status) => status switch
    {
        "FAILED" => "Failed",
        "TIMED_OUT" => "Timed out",
        "ABORTED" => "Aborted",
        _ => status,
    };

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } v && v != default ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : LogRedactor.SanitiseForLog(LogRedactor.Redact(text));

    private static string Region(Namespace ns) => string.IsNullOrWhiteSpace(ns.AwsRegion) ? "us-east-1" : ns.AwsRegion;

    private static string MachineLink(Namespace ns, string arn) =>
        $"https://{Region(ns)}.console.aws.amazon.com/states/home?region={Region(ns)}#/statemachines/view/{Uri.EscapeDataString(arn)}";

    private static string ExecutionLink(Namespace ns, string arn) =>
        $"https://{Region(ns)}.console.aws.amazon.com/states/home?region={Region(ns)}#/v2/executions/details/{Uri.EscapeDataString(arn)}";

    private static Error Fail(Exception ex) => ex is AmazonStepFunctionsException { ErrorCode: "AccessDeniedException" or "AccessDenied" or "UnrecognizedClientException" }
        ? Error.Forbidden("AWS.StepFunctions.NotEnabled",
            "Not enabled for this connection: its credential may not read Step Functions (needs states:ListStateMachines, states:ListExecutions, states:DescribeExecution).")
        : Error.ExternalService("AWS.StepFunctions.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
