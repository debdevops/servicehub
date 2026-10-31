using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Components;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Infrastructure.Recovery;

/// <summary>
/// Asks a cloud to redrive one failed run of a component that is not a message store (build-plan unit PP5).
/// </summary>
/// <remarks>
/// The order is the same safety as a replay: <b>gate → open a ledger entry → touch the cloud → record what the cloud said</b>. Only a person
/// may start it (the gate denies an Agent or a rule), only on a component whose catalogue class offers a cloud redrive, and never on a
/// read-only connection. The run's input is never read: the cloud restarts the run itself. What the cloud said is recorded with
/// <see cref="CancellationToken.None"/>, and a redrive is never retried. The entry ends <c>Unverified</c>: acceptance says nothing about
/// whether the run then finished fixed. No provider is named here (R4); the mover is found by component key and adapter.
/// </remarks>
public sealed class ComponentRedriveService : IComponentRedriveService
{
    private readonly IRecoveryLedger _ledger;
    private readonly IRecoveryEligibilityGate _gate;
    private readonly IReadOnlyList<IComponentMoveOn> _movers;
    private readonly IAuditTrail _audit;
    private readonly ILogger<ComponentRedriveService> _logger;
    private readonly Func<string, RecoverabilityClass?> _classOf;

    /// <summary>Creates the service.</summary>
    public ComponentRedriveService(
        IRecoveryLedger ledger, IRecoveryEligibilityGate gate, IEnumerable<IComponentMoveOn> movers, IAuditTrail audit, ILogger<ComponentRedriveService> logger)
        : this(ledger, gate, movers, audit, logger, ClassFromCatalogue)
    {
    }

    // Tests choose the class, because the real catalogue offers no redrive until a live run is recorded.
    internal ComponentRedriveService(
        IRecoveryLedger ledger, IRecoveryEligibilityGate gate, IEnumerable<IComponentMoveOn> movers, IAuditTrail audit, ILogger<ComponentRedriveService> logger,
        Func<string, RecoverabilityClass?> classOf)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _movers = [.. (movers ?? throw new ArgumentNullException(nameof(movers)))];
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _classOf = classOf ?? throw new ArgumentNullException(nameof(classOf));
    }

    /// <inheritdoc />
    public async Task<Result<RedriveCheck>> CheckAsync(Namespace ns, string componentKey, string itemId, RecoveryActor actor, CancellationToken cancellationToken)
    {
        var resolved = Resolve(ns, componentKey, itemId);
        if (resolved.Failure is not null)
        {
            return Result<RedriveCheck>.Failure(resolved.Failure);
        }

        var mover = resolved.Mover!;
        var decision = await EvaluateAsync(ns, componentKey, itemId, mover.PlaceOf(itemId), actor, cancellationToken);
        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            return Result<RedriveCheck>.Success(new RedriveCheck(false, decision.ReasonCode, Refusal(decision.ReasonCode, mover.Verb), mover.Verb));
        }

        var previous = await _ledger.GetLastAcceptedRequestAtAsync(ns.OwnerId, ns.Id, itemId, cancellationToken);
        return Result<RedriveCheck>.Success(new RedriveCheck(true, null, $"A person can ask {CloudName(ns)} to {mover.Verb} this run.", mover.Verb, previous));
    }

    /// <inheritdoc />
    public async Task<Result<RedriveOutcome>> RequestRedriveAsync(
        Namespace ns, string componentKey, string itemId, RecoveryActor actor, string? intentHeader, string? correlationId, CancellationToken cancellationToken)
    {
        var resolved = Resolve(ns, componentKey, itemId);
        if (resolved.Failure is not null)
        {
            return Result<RedriveOutcome>.Failure(resolved.Failure);
        }

        var mover = resolved.Mover!;
        var place = mover.PlaceOf(itemId);
        var decision = await EvaluateAsync(ns, componentKey, itemId, place, actor, cancellationToken);
        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            await AuditAsync(ns, actor, itemId, place, "refused", decision.ReasonCode, correlationId, CancellationToken.None);
            if (decision.ReasonCode == EscalationReasons.ReplayOutcomeUnknown)
            {
                // Not a permission problem: an earlier request has no answer, and a person has to say what happened to it.
                return Result<RedriveOutcome>.Failure(Error.Conflict(EscalationReasons.ReplayOutcomeUnknown, EscalationReasons.Describe(EscalationReasons.ReplayOutcomeUnknown)));
            }

            var refusal = Refusal(decision.ReasonCode, mover.Verb);
            return Result<RedriveOutcome>.Failure(decision.Verdict == EligibilityVerdict.Deny
                ? Error.Forbidden(decision.ReasonCode ?? "DENIED", refusal)
                : Error.Conflict(decision.ReasonCode ?? "ESCALATED", refusal));
        }

        // From here the attempt is on the record whatever happens.
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ns.OwnerId, Kind = RecoveryOperationKind.Redrive, Trigger = RecoveryTrigger.Manual, Actor = actor, IntentHeader = intentHeader,
            NamespaceId = ns.Id, NamespaceNameSnapshot = ns.Name, ProviderSnapshot = ns.Provider, EnvironmentSnapshot = ns.Environment,
            ScopeDescription = $"component={componentKey}; run={itemId}", CorrelationId = correlationId, TargetCount = 1,
        }, CancellationToken.None);
        if (operation.IsFailure)
        {
            return Result<RedriveOutcome>.Failure(operation.Error);
        }

        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id, OwnerId = ns.OwnerId, Actor = actor, NamespaceId = ns.Id, NamespaceNameSnapshot = ns.Name,
            ProviderSnapshot = ns.Provider, EnvironmentSnapshot = ns.Environment, EntityNameSnapshot = place, EntityTypeSnapshot = "execution",
            SourceMessageIdSnapshot = itemId, BodyHash = HashOf(itemId), TargetEntity = place,
        }, CancellationToken.None);
        if (entry.IsFailure)
        {
            return Result<RedriveOutcome>.Failure(entry.Error);
        }

        // The cloud call. An exception means we do not know what happened — that is its own state, never "failed".
        RecoveryExecutionOutcome executed;
        string? errorCode = null;
        string? errorMessage = null;
        try
        {
            var result = await mover.RedriveAsync(ns, itemId, entry.Value.Id.ToString("N"), cancellationToken);
            if (result.IsSuccess)
            {
                executed = RecoveryExecutionOutcome.Accepted;
            }
            else if (NothingCanHaveHappened(result.Error))
            {
                executed = RecoveryExecutionOutcome.Rejected;
                errorCode = result.Error.Code;
                errorMessage = LogRedactor.SanitiseForLog(result.Error.Message);
            }
            else
            {
                executed = RecoveryExecutionOutcome.Unknown;
                errorCode = result.Error.Code;
                errorMessage = "ServiceHub lost contact with the cloud before it could tell whether the redrive was accepted. Check the run before asking again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redrive of {Item} threw; recording its outcome as unknown", LogRedactor.SanitiseForLog(itemId));
            executed = RecoveryExecutionOutcome.Unknown;
            errorMessage = "ServiceHub lost contact with the cloud before it could tell whether the redrive was accepted. Check the run before asking again.";
        }

        var recorded = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id, OwnerId = ns.OwnerId, Actor = actor, Outcome = executed,
            ProviderDetailJson = errorCode is null ? null : System.Text.Json.JsonSerializer.Serialize(new { errorCode }),
        }, CancellationToken.None);
        var final = recorded.IsSuccess ? recorded.Value : entry.Value;

        var status = executed.ToString().ToLowerInvariant();
        await AuditAsync(ns, actor, itemId, place, status, errorCode, correlationId, CancellationToken.None);

        var sentence = executed switch
        {
            RecoveryExecutionOutcome.Accepted => $"{CloudName(ns)} accepted the {mover.Verb} request. ServiceHub cannot confirm the run finished fixed.",
            RecoveryExecutionOutcome.Rejected => errorMessage ?? $"{CloudName(ns)} did not accept it, so nothing was changed.",
            _ => errorMessage ?? $"Whether {CloudName(ns)} accepted it is not known. Check the run before asking again.",
        };
        return Result<RedriveOutcome>.Success(new RedriveOutcome(entry.Value.Id, operation.Value.Id, status, final.State.ToString(), sentence, errorCode));
    }

    private readonly record struct Resolved(IComponentMoveOn? Mover, Error? Failure);

    private Resolved Resolve(Namespace ns, string componentKey, string itemId)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var mover = _movers.FirstOrDefault(m => m.ComponentKey == componentKey && m.Provider == ns.Provider);
        if (mover is null)
        {
            return new Resolved(null, Error.NotFound(ErrorCodes.NotFound, $"'{componentKey}' cannot be redriven on this connection."));
        }

        return string.IsNullOrWhiteSpace(itemId) || !mover.Owns(ns, itemId.Trim())
            ? new Resolved(null, Error.Validation(ErrorCodes.ValidationFailed, "That is not a run of this component in this connection's account and region."))
            : new Resolved(mover, null);
    }

    private Task<EligibilityDecision> EvaluateAsync(Namespace ns, string componentKey, string itemId, string place, RecoveryActor actor, CancellationToken cancellationToken) =>
        _gate.EvaluateAsync(
            new RecoveryEligibilityRequest(
                ns.OwnerId, RecoveryOperationKind.Redrive, actor.Kind, RecoveryTrigger.Manual, ns.Id, place, BodyHash: null, SignatureHash: null, ns.Environment,
                Provider: ns.Provider, SourceMessageId: itemId, ConnectionCanSend: ns.CanSend, Recoverability: _classOf(componentKey)),
            cancellationToken);

    private static RecoverabilityClass? ClassFromCatalogue(string componentKey) =>
        ComponentCatalog.All.FirstOrDefault(c => c.ComponentKey == componentKey && ComponentAccessRules.ReadsOnDemand(c))?.Class;

    // The cloud's own name for the screen, read from the catalogue (the one place that ties an adapter to a cloud).
    private static string CloudName(Namespace ns) =>
        ComponentCatalog.CloudOf(ns.Provider) is { } cloud ? ComponentCatalog.CloudName(cloud) : "The cloud";

    private static string Refusal(string? code, string verb) => code switch
    {
        "REDRIVE_AUTOMATION_PROHIBITED" => $"Only a person can {verb} a run, never an Agent or a rule.",
        "REDRIVE_NOT_OFFERED" => $"A {verb} is not offered for this component yet. It is handed to a person, who re-runs it in the cloud console.",
        "CONNECTION_READ_ONLY" => "This connection is marked read-only, so ServiceHub will not change anything in it.",
        "PRODUCTION_ELEVATION_REQUIRED" => "This is a Production connection, and a Production change needs an approved elevation.",
        _ => $"A {verb} is not allowed here ({code ?? "refused"}).",
    };

    private static string HashOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>True for refusals that happen before or instead of the cloud acting. Anything else (internal, timeout, upstream) leaves the outcome unknown.</summary>
    private static bool NothingCanHaveHappened(Error error) =>
        error.Type is ErrorType.Validation or ErrorType.NotFound or ErrorType.Unauthorized or ErrorType.Forbidden
            or ErrorType.Conflict or ErrorType.BusinessRule or ErrorType.RateLimited;

    private async Task AuditAsync(Namespace ns, RecoveryActor actor, string itemId, string place, string outcome, string? error, string? correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(new AuditLog
            {
                Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = ns.OwnerId, UserIdentity = actor.Identity,
                Action = AuditActions.RedriveExecution, Outcome = outcome == "accepted" ? AuditActions.Success : AuditActions.Failure, NamespaceId = ns.Id, NamespaceName = ns.Name,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(), Environment = ns.Environment.ToString(),
                EntityName = place, ResourceName = itemId,
                ErrorDetails = error is null ? (outcome == "accepted" ? null : outcome) : LogRedactor.SanitiseForLog(error), CorrelationId = correlationId,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record the redrive of {Item} in the audit trail", LogRedactor.SanitiseForLog(itemId));
        }
    }
}
