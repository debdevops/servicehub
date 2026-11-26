using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

using ServiceHub.Providers.Aws.Components;

namespace ServiceHub.Providers.Aws.MoveOn;

/// <summary>
/// Moves a failed Step Functions run on with AWS's own <c>RedriveExecution</c> (build-plan unit PP5). AWS restarts a failed
/// <b>Standard</b> run from the state that failed, with the same input, inside AWS, so ServiceHub never reads the input.
/// </summary>
/// <remarks>
/// Needs <c>states:RedriveExecution</c>, which the read access this component documents does not include; a connection without it is told so
/// and ServiceHub never grants it. Nothing here is reachable except through the gated redrive service.
/// </remarks>
public sealed class StepFunctionsMoveOn : IComponentMoveOn
{
    private readonly IAwsClientFactory _clients;

    /// <summary>Creates the mover.</summary>
    public StepFunctionsMoveOn(IAwsClientFactory clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    /// <inheritdoc />
    public string ComponentKey => StepFunctionsSource.Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Aws;

    /// <inheritdoc />
    public string Verb => "redrive";

    /// <inheritdoc />
    public bool Owns(Namespace ns, string itemId)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var parts = (itemId ?? string.Empty).Split(':');
        var region = string.IsNullOrWhiteSpace(ns.AwsRegion) ? "us-east-1" : ns.AwsRegion.Trim();
        return itemId is { Length: <= 1024 } && parts.Length >= 8 && parts[0] == "arn" && parts[1].StartsWith("aws", StringComparison.Ordinal)
            && parts[2] == "states" && string.Equals(parts[3], region, StringComparison.OrdinalIgnoreCase) && parts[5] == "execution";
    }

    /// <inheritdoc />
    public string PlaceOf(string itemId)
    {
        // arn:aws:states:region:account:execution:{machine}:{run}  ->  arn:aws:states:region:account:stateMachine:{machine}
        var parts = itemId.Split(':');
        return string.Join(':', parts.Take(5)) + ":stateMachine:" + parts[6];
    }

    /// <inheritdoc />
    public async Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestToken);
        if (!Owns(ns, itemId))
        {
            return Result.Failure<MoveOnAcceptance>(Error.Validation("AWS.StepFunctions.InvalidItem", "That is not a Step Functions execution in this connection's account and region."));
        }

        try
        {
            var answer = await _clients.GetStepFunctionsClient(ns)
                .RedriveExecutionAsync(new RedriveExecutionRequest { ExecutionArn = itemId, ClientToken = requestToken }, ct).ConfigureAwait(false);
            DateTimeOffset? at = answer.RedriveDate == default ? null : new DateTimeOffset(DateTime.SpecifyKind(answer.RedriveDate, DateTimeKind.Utc));
            return Result.Success(new MoveOnAcceptance(at));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cloud failure becomes a result the service can record, never an exception.
        catch (Exception ex)
        {
            return Result.Failure<MoveOnAcceptance>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    // The errors AWS gives when it did nothing are told apart from the ones that leave it unknown: the service records the first as
    // "rejected" (safe to try again later) and the rest as "unknown" (check the run before trying again).
    private static Error Fail(Exception ex) => ex switch
    {
        AmazonStepFunctionsException { ErrorCode: "AccessDeniedException" or "AccessDenied" or "UnrecognizedClientException" } =>
            Error.Forbidden("AWS.StepFunctions.RedriveNotAllowed", "This connection's credential may not redrive runs (it needs states:RedriveExecution). ServiceHub never grants it."),
        ExecutionNotRedrivableException =>
            Error.Conflict("AWS.StepFunctions.NotRedrivable", "AWS cannot redrive this run: it must be a Standard workflow, closed as failed, timed out or aborted, within 14 days, and the state machine must not have changed in a way that breaks it."),
        ExecutionDoesNotExistException =>
            Error.NotFound("AWS.StepFunctions.ExecutionGone", "AWS no longer has this run."),
        ExecutionLimitExceededException =>
            Error.RateLimited("AWS.StepFunctions.RedriveLimit", "AWS has reached its limit of running executions for this state machine. Try again later."),
        InvalidArnException =>
            Error.Validation("AWS.StepFunctions.InvalidItem", "AWS did not accept that run id."),
        _ => Error.ExternalService("AWS.StepFunctions.RedriveFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message))),
    };
}
