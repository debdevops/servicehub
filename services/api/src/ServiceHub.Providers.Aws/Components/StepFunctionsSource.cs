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
    public async Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var machines = await ListMachinesAsync(ns, ct).ConfigureAwait(false);
            return Result.Success(new SourcePlacesPage(
                [.. machines.Items.Select(m => new SourcePlace(m.StateMachineArn, m.Name, "state machine", MachineLink(ns, m.StateMachineArn)))], machines.More));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<SourcePlacesPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (placeId is not null && !IsStateMachineArn(ns, placeId))
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("AWS.StepFunctions.InvalidPlace", "That is not a state machine in this connection's region."));
        }

        // The whole listing shares one time budget. Running out of it is not the caller cancelling: it returns what was read.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var client = _clients.GetStepFunctionsClient(ns);
            var reached = false;
            List<string> arns;
            if (placeId is null)
            {
                var machines = await ListMachinesAsync(ns, budget.Token).ConfigureAwait(false);
                arns = [.. machines.Items.Take(MaxMachinesPerListing).Select(m => m.StateMachineArn)];
                reached = machines.Items.Count > MaxMachinesPerListing || machines.More;
            }
            else
            {
                arns = [placeId];
            }

            var reads = arns.SelectMany(arn => Statuses.Select(status => (Arn: arn, Status: status))).ToList();
            var results = await SourceReads.RunAsync(
                reads, (r, token) => ReadOneAsync(ns, client, r.Arn, r.Status, token), Fail, new Read([], false), budget.Token, ct).ConfigureAwait(false);

            var incomplete = results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("AWS.StepFunctions.ReadFailed", "Step Functions could not be read."));
            }

            reached |= results.Any(r => r.Value.More);
            var ordered = results.SelectMany(r => r.Value.Items).OrderByDescending(i => i.AtUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems)], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("AWS.StepFunctions.ReadTimedOut", "Reading Step Functions took too long. Try one state machine."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<FailedItemsPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    private sealed record Read(IReadOnlyList<FailedItem> Items, bool More);

    private async Task<Read> ReadOneAsync(Namespace ns, IAmazonStepFunctions client, string arn, string status, CancellationToken ct)
    {
        var response = await client.ListExecutionsAsync(
            new ListExecutionsRequest { StateMachineArn = arn, StatusFilter = status, MaxResults = ComponentSourceLimits.MaxFailedItems }, ct).ConfigureAwait(false);
        var items = response.Executions
            .Select(e => new FailedItem(e.ExecutionArn, arn, Utc(e.StopDate), status, ReasonCode: null, $"{Words(status)}: {Clean(e.Name)}", CopyKind.None, ExecutionLink(ns, e.ExecutionArn)))
            .ToList();
        return new Read(items, !string.IsNullOrEmpty(response.NextToken));
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (!IsExecutionArn(ns, itemId))
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("AWS.StepFunctions.InvalidItem", "That is not an execution in this connection's region."));
        }

        try
        {
            var e = await _clients.GetStepFunctionsClient(ns).DescribeExecutionAsync(new DescribeExecutionRequest { ExecutionArn = itemId }, ct).ConfigureAwait(false);
            var status = e.Status?.Value ?? "UNKNOWN";
            var item = new FailedItem(
                e.ExecutionArn, e.StateMachineArn, Utc(e.StopDate), status, Clean(e.Error), $"{Words(status)}: {Clean(e.Name)}", CopyKind.None, ExecutionLink(ns, e.ExecutionArn));

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

    private static string Region(Namespace ns) => string.IsNullOrWhiteSpace(ns.AwsRegion) ? "us-east-1" : ns.AwsRegion.Trim();

    // The ARN's partition is aws, aws-cn or aws-us-gov. Only a Step Functions ARN in this connection's own region is read.
    private static bool IsStateMachineArn(Namespace ns, string arn) => IsArn(ns, arn, "stateMachine");

    private static bool IsExecutionArn(Namespace ns, string arn) => IsArn(ns, arn, "execution");

    private static bool IsArn(Namespace ns, string arn, string resource)
    {
        var parts = arn.Split(':');
        return parts.Length >= 7 && parts[0] == "arn" && parts[1].StartsWith("aws", StringComparison.Ordinal) && parts[2] == "states"
            && string.Equals(parts[3], Region(ns), StringComparison.OrdinalIgnoreCase) && parts[5] == resource && arn.Length <= 1024;
    }

    private static string ConsoleHost(Namespace ns)
    {
        var region = Region(ns);
        return region.StartsWith("cn-", StringComparison.Ordinal) ? $"{region}.console.amazonaws.cn"
            : region.StartsWith("us-gov-", StringComparison.Ordinal) ? $"{region}.console.amazonaws-us-gov.com"
            : $"{region}.console.aws.amazon.com";
    }

    private static string MachineLink(Namespace ns, string arn) =>
        $"https://{ConsoleHost(ns)}/states/home?region={Region(ns)}#/statemachines/view/{Uri.EscapeDataString(arn)}";

    private static string ExecutionLink(Namespace ns, string arn) =>
        $"https://{ConsoleHost(ns)}/states/home?region={Region(ns)}#/v2/executions/details/{Uri.EscapeDataString(arn)}";

    private static Error Fail(Exception ex) => ex is AmazonStepFunctionsException { ErrorCode: "AccessDeniedException" or "AccessDenied" or "UnrecognizedClientException" }
        ? Error.Forbidden("AWS.StepFunctions.NotEnabled",
            "Not enabled for this connection: its credential may not read Step Functions (needs states:ListStateMachines, states:ListExecutions, states:DescribeExecution).")
        : Error.ExternalService("AWS.StepFunctions.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
