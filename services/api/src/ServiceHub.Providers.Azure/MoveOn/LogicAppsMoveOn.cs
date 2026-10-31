using System.Net;
using System.Text.RegularExpressions;
using Azure.Identity;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.Providers.Azure.MoveOn;

/// <summary>
/// Moves a failed Logic Apps run on with Azure's own trigger-history <c>resubmit</c> (build-plan unit R2). <b>This is not a redrive</b>:
/// Azure starts a <i>new</i> run from the trigger, with the trigger's original input, and every action runs again, including those that
/// already succeeded. The original run stays <c>Failed</c>. ServiceHub never reads the trigger's input or any action's input or output.
/// </summary>
/// <remarks>
/// Needs <c>Microsoft.Logic/workflows/triggers/histories/resubmit/action</c>, which the Reader role and the Logic App Operator role do not
/// include (Logic App Contributor does); a connection without it is told so, and ServiceHub never grants it. Azure gives no idempotency
/// key, so a request is never retried. Nothing here is reachable except through the gated redrive service.
/// </remarks>
internal sealed partial class LogicAppsMoveOn : IComponentMoveOn
{
    private const string ApiVersion = "2016-06-01";

    // Only a run that ended badly may be resubmitted: never one that succeeded, and never one still going.
    private static readonly string[] Resubmittable = ["Failed", "TimedOut", "Cancelled"];

    private readonly IAzureLogicAppsReader _reader;
    private readonly IAzureResubmitClient _resubmit;

    public LogicAppsMoveOn(IAzureRestClient rest, IAzureResubmitClient resubmit)
        : this(new AzureLogicAppsReader(rest ?? throw new ArgumentNullException(nameof(rest))), resubmit)
    {
    }

    internal LogicAppsMoveOn(IAzureLogicAppsReader reader, IAzureResubmitClient resubmit)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _resubmit = resubmit ?? throw new ArgumentNullException(nameof(resubmit));
    }

    public string ComponentKey => LogicAppsSource.Key;

    public CloudProviderType Provider => CloudProviderType.Azure;

    public string Verb => "resubmit";

    // Azure itself enforces which subscriptions the identity may act in (a run elsewhere is answered 403); only the shape is checked here.
    public bool Owns(Namespace ns, string itemId)
    {
        ArgumentNullException.ThrowIfNull(ns);
        return itemId is not null && LogicAppsSource.IsRunId(itemId);
    }

    public string PlaceOf(string itemId) => LogicAppsSource.WorkflowOf(itemId);

    public async Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestToken);
        if (!Owns(ns, itemId))
        {
            return Result.Failure<MoveOnAcceptance>(Error.Validation("Azure.LogicApps.InvalidItem", "That is not a Logic Apps run."));
        }

        try
        {
            // Read the run first, for two things only: that it ended badly right now, and the name of the trigger that started it.
            var run = await _reader.GetRunAsync(ns, itemId, ct).ConfigureAwait(false);
            if (run is null)
            {
                return Result.Failure<MoveOnAcceptance>(Error.NotFound("Azure.LogicApps.RunGone", "Azure no longer has this run."));
            }

            if (!Resubmittable.Contains(run.Run.Status, StringComparer.OrdinalIgnoreCase))
            {
                return Result.Failure<MoveOnAcceptance>(Error.Conflict(
                    "Azure.LogicApps.NotResubmittable", $"Only a run that ended Failed, TimedOut or Cancelled can be resubmitted; this one is {run.Run.Status}."));
            }

            if (run.TriggerName is not { } trigger || !TriggerNameRegex().IsMatch(trigger))
            {
                return Result.Failure<MoveOnAcceptance>(Error.Conflict(
                    "Azure.LogicApps.NoTrigger", "Azure does not say which trigger started this run, so it cannot be resubmitted from here."));
            }

            // The history is named after the run for a trigger that resulted in a run (Microsoft Learn, Workflow Trigger Histories - Resubmit).
            var history = itemId[(itemId.LastIndexOf('/') + 1)..];
            var url = AzureArm.Url(
                $"{PlaceOf(itemId)}/triggers/{Uri.EscapeDataString(trigger)}/histories/{Uri.EscapeDataString(history)}/resubmit", ApiVersion);
            await _resubmit.PostAsync(ns, url, ct).ConfigureAwait(false);
            return Result.Success(new MoveOnAcceptance(null));
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

    // The errors Azure gives when it did nothing are told apart from the ones that leave it unknown: the service records the first as
    // "rejected" (safe to try again later) and the rest as "unknown" (check the run before trying again).
    private static Error Fail(Exception ex) => ex switch
    {
        AzureNotEntraException =>
            Error.Forbidden("Azure.LogicApps.NotEnabled", "This connection signs in with a Service Bus connection string or a stored secret. A resubmit needs an Azure connection that signs in with Entra."),
        AzureRestException { Status: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            Error.Forbidden("Azure.LogicApps.ResubmitNotAllowed", "This connection's identity may not resubmit runs (it needs Microsoft.Logic/workflows/triggers/histories/resubmit/action, which Reader and Logic App Operator do not include). ServiceHub never grants it."),
        AzureRestException { Status: HttpStatusCode.NotFound } =>
            Error.NotFound("Azure.LogicApps.ResubmitGone", "Azure no longer has this run's trigger history, so it cannot be resubmitted."),
        AzureRestException { Status: HttpStatusCode.Conflict } =>
            Error.Conflict("Azure.LogicApps.ResubmitConflict", "Azure refused the resubmit because the Logic App is not in a state that allows it (for example it is disabled)."),
        AzureRestException { Status: HttpStatusCode.BadRequest } =>
            Error.Validation("Azure.LogicApps.ResubmitInvalid", "Azure did not accept the resubmit request for this run."),
        AzureRestException { Status: HttpStatusCode.TooManyRequests } =>
            Error.RateLimited("Azure.LogicApps.ResubmitLimit", "Azure is limiting requests. Try again later."),
        AuthenticationFailedException or CredentialUnavailableException =>
            Error.Forbidden("Azure.LogicApps.NotEnabled", "ServiceHub could not sign in to Azure with Entra. Check the identity this server runs as."),
        _ => Error.ExternalService("Azure.LogicApps.ResubmitFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message))),
    };

    // A trigger name is letters, digits, underscore, hyphen, dot and parentheses; anything else is never put in an address.
    [GeneratedRegex(@"^[A-Za-z0-9_\-.()]{1,80}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex TriggerNameRegex();
}
