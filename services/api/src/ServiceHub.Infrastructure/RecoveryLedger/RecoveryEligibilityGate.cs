using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IRecoveryEligibilityGate"/>
/// </summary>
/// <remarks>
/// Predicate 3 (recurrence cap) is Phase A's inline <c>AutoReplayExecutor</c> lineage check,
/// relocated here (roadmap §26 Phase B) so every caller shares it, not just auto-replay, and
/// made actor-conditional per the accepted Option B roadmap decision: Automation/System hits a
/// hard stop at the cap, while User/ApiKey continues through the rest of the gate — a human
/// remains the actor of record and is not auto-denied merely for repeating an attempt a human
/// chose to make again. Predicate 4 (rate limit) deliberately does not re-implement
/// <c>IAutoReplayExecutor.CanReplayAsync</c> — it consumes that method's existing result via
/// <see cref="RecoveryEligibilityRequest.RateLimitExceeded"/>, since only rule-driven callers have
/// a per-rule limit to check and duplicating that query here would violate the "don't duplicate
/// existing safety checks" instruction.
/// </remarks>
public sealed class RecoveryEligibilityGate : IRecoveryEligibilityGate
{
    // Roadmap §7.5 item 6: the cap is a fixed count (3 prior lineage-matched entries block a 4th
    // attempt), never configurable. The 90-day window is a performance bound only, not a safety
    // parameter. Unchanged from AutoReplayExecutor's original Phase A implementation.
    // Public so callers (e.g. AutoReplayExecutor's error message) can reference the exact cap
    // without duplicating the literal.
    public const int RecurrenceLineageCap = 3;
    private static readonly TimeSpan RecurrenceLookbackWindow = TimeSpan.FromDays(90);

    private const string ReasonEmergencyStopActive = "EMERGENCY_STOP_ACTIVE";
    private const string ReasonEmergencyStopQueryError = "EMERGENCY_STOP_QUERY_ERROR";
    private const string ReasonPurgeAutomationProhibited = "PURGE_AUTOMATION_PROHIBITED";
    private const string ReasonProductionElevationRequired = "PRODUCTION_ELEVATION_REQUIRED";
    private const string ReasonAmbiguousCollision = "RECURRENCE_CAP_AMBIGUOUS_COLLISION";
    private const string ReasonExceededExact = "RECURRENCE_CAP_EXCEEDED";
    private const string ReasonExceededHeuristic = "RECURRENCE_CAP_EXCEEDED_HEURISTIC";
    private const string ReasonQueryError = "RECURRENCE_CAP_QUERY_ERROR";
    private const string ReasonAutonomySignatureHashMissing = "AUTONOMY_SIGNATURE_HASH_MISSING";
    private const string ReasonAutonomyGrantQueryError = "AUTONOMY_GRANT_QUERY_ERROR";
    private const string ReasonAutonomyGrantInsufficient = "AUTONOMY_GRANT_INSUFFICIENT";

    /// <summary>
    /// Predicate 4's reason code. Never <c>Declined</c>-recorded (roadmap §9.3): "routine
    /// operational throttling with its own adequate existing log line, not a safety escalation."
    /// Callers check for this exact code to know when to skip
    /// <see cref="IRecoveryLedger.RecordDeclinedAsync"/>.
    /// </summary>
    public const string ReasonRateLimited = "RATE_LIMITED";

    private readonly IRecoveryLedger _recoveryLedger;
    private readonly ILogger<RecoveryEligibilityGate> _logger;

    /// <summary>Initialises a new instance of <see cref="RecoveryEligibilityGate"/>.</summary>
    public RecoveryEligibilityGate(IRecoveryLedger recoveryLedger, ILogger<RecoveryEligibilityGate> logger)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EligibilityDecision> EvaluateAsync(
        RecoveryEligibilityRequest request, CancellationToken cancellationToken = default)
    {
        // Predicate 0 — emergency stop (§9.4.2, §15.2): owner-scoped kill switch on new
        // Automation/System-originated execution only, ahead of every other predicate.
        // User/ApiKey requests are never subject to it — skip the read entirely for them, so
        // "never affects manual recovery" holds regardless of the query's outcome. State is read
        // live from the ledger on every evaluation: no stored flag, no second table.
        if (request.ActorKind is RecoveryActorKind.Automation or RecoveryActorKind.System)
        {
            bool emergencyStopActive;
            try
            {
                emergencyStopActive = await _recoveryLedger.IsEmergencyStopActiveAsync(request.OwnerId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Fail closed on a query error (roadmap §18): an unrunnable safety check must
                // block automation, never silently allow it through.
                _logger.LogError(ex,
                    "Emergency-stop state query failed for owner {OwnerId}; failing closed", request.OwnerId);
                return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonEmergencyStopQueryError);
            }

            if (emergencyStopActive)
            {
                return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonEmergencyStopActive);
            }
        }

        // Predicate 1 — purge origin (§9.1): unconditional, non-overridable. No purge-capable
        // path may resolve ActorKind = Automation/System once the §29.10 actor-propagation fix
        // ships — a human-requested bulk/signature purge carries User/ApiKey and passes through.
        if (request.ActionKind == RecoveryOperationKind.Purge
            && request.ActorKind is RecoveryActorKind.Automation or RecoveryActorKind.System)
        {
            return new EligibilityDecision(EligibilityVerdict.Deny, ReasonPurgeAutomationProhibited);
        }

        // Predicate 2 — production elevation (§9): no elevation-recording mechanism exists yet in
        // v3.7.0, so this is unconditional. Every existing call site already blocks Prod ahead of
        // this point (MessagesController, BulkOperationExecutor, SignatureReplayExecutor,
        // DlqMonitorWorker's AutoReplayRule scan) — this predicate is defense-in-depth here, not a
        // live behavior change for any current caller.
        if (request.Environment == EnvironmentType.Prod)
        {
            return new EligibilityDecision(EligibilityVerdict.Deny, ReasonProductionElevationRequired);
        }

        // Predicate 3 — recurrence cap (§7.5): only evaluable when the caller supplied a real
        // message identity, which every current wiring target does.
        // §9.4.1: a User/ApiKey actor hitting the cap is not auto-denied (Option B) — the
        // reason/count are carried forward as observability context on the eventual Allow verdict
        // rather than silently discarded, and evaluation continues through predicates 4/5.
        EligibilityDecision? recurrenceContext = null;
        if (request.NamespaceId is { } namespaceId
            && !string.IsNullOrEmpty(request.EntityNameSnapshot)
            && !string.IsNullOrEmpty(request.BodyHash))
        {
            var lineageDecision = await EvaluateRecurrenceLineageAsync(
                request.OwnerId, namespaceId, request.EntityNameSnapshot, request.BodyHash,
                request.ActorKind, cancellationToken);
            if (lineageDecision is not null)
            {
                if (lineageDecision.Verdict != EligibilityVerdict.Allow)
                {
                    return lineageDecision;
                }

                recurrenceContext = lineageDecision;
            }
        }

        // Predicate 4 — rate limit (§9: "wraps the existing CanReplayAsync"). Pre-computed by the
        // caller; false for every caller with no per-rule rate-limit concept.
        if (request.RateLimitExceeded)
        {
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonRateLimited);
        }

        // Predicate 5 — autonomy lookup (§9, enforced per §9.4.3): an Automation actor may only
        // execute unattended once its exact (OwnerId, SignatureHash, ActionKind) triple has earned
        // AutonomyGrant.CurrentLevel Standing (L4) or Unattended (L5) — L3 (Approve, the permanent
        // human floor) and no-grant-at-all both escalate identically, since neither has earned
        // unattended execution. Never applies to User/ApiKey — a human remains free to act at any
        // time regardless of grant state; only unattended automation needs one.
        if (request.ActorKind == RecoveryActorKind.Automation)
        {
            var autonomyDecision = await EvaluateAutonomyGrantAsync(request, cancellationToken);
            if (autonomyDecision is not null)
            {
                return autonomyDecision;
            }
        }

        return recurrenceContext ?? EligibilityDecision.Allow;
    }

    private async Task<EligibilityDecision?> EvaluateAutonomyGrantAsync(
        RecoveryEligibilityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.SignatureHash))
        {
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonAutonomySignatureHashMissing);
        }

        AutonomyGrant? grant;
        try
        {
            grant = await _recoveryLedger.GetAutonomyGrantAsync(
                request.OwnerId, request.SignatureHash, request.ActionKind, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail closed on a query error (roadmap §18): an unrunnable safety check must block
            // automation, never silently allow it through.
            _logger.LogError(ex,
                "AutonomyGrant query failed for owner {OwnerId} signature {SignatureHash}; failing closed",
                request.OwnerId, request.SignatureHash);
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonAutonomyGrantQueryError);
        }

        var currentLevel = grant?.CurrentLevel ?? AutonomyLevel.Approve;
        if (currentLevel is AutonomyLevel.Standing or AutonomyLevel.Unattended)
        {
            return null;
        }

        _logger.LogInformation(
            "Eligibility Gate predicate 5 (autonomy lookup) escalating for owner {OwnerId} signature " +
            "{SignatureHash} — current level {Level} has not earned unattended execution (roadmap §9.4.3)",
            request.OwnerId, request.SignatureHash, currentLevel);
        return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonAutonomyGrantInsufficient);
    }

    private async Task<EligibilityDecision?> EvaluateRecurrenceLineageAsync(
        string ownerId, Guid namespaceId, string entityName, string bodyHash,
        RecoveryActorKind actorKind, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecoveryLedgerEntry> matches;
        try
        {
            matches = await _recoveryLedger.FindLineageMatchesAsync(
                ownerId, namespaceId, entityName, bodyHash,
                DateTimeOffset.UtcNow - RecurrenceLookbackWindow, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail closed on a query error (roadmap §18): an unrunnable safety check must block,
            // not silently allow the attempt through. Actor-unconditional — this is an
            // infrastructure failure, not the recurrence-cap-reached case Option B addresses.
            _logger.LogError(ex,
                "Recurrence-lineage query failed for owner {OwnerId}; failing closed", ownerId);
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonQueryError);
        }

        if (matches.Count < RecurrenceLineageCap)
        {
            return null;
        }

        var distinctSignatures = matches
            .Select(e => e.SignatureHashSnapshot)
            .Where(s => s is not null)
            .Distinct()
            .ToList();

        // A matched entry not corroborated by a marker (MarkerApplied == false) or whose own
        // past recurrence was only heuristically confirmed needs SignatureHashSnapshot agreement
        // with the rest of the matched set before it's trusted as the same lineage (roadmap §7.5
        // items 2–3); when every matched entry is Exact-confidence, BodyHash alone already
        // suffices and a coincidental cross-signature match elsewhere can't apply.
        var hasUncorroboratedEntry = matches.Any(e =>
            !e.MarkerApplied || e.VerificationConfidence == VerificationConfidence.Heuristic);

        var reasonCode = distinctSignatures.Count > 1 && hasUncorroboratedEntry
            ? ReasonAmbiguousCollision
            : matches.Any(e => e.VerificationConfidence == VerificationConfidence.Exact)
                ? ReasonExceededExact
                : ReasonExceededHeuristic;

        // Option B (accepted roadmap decision): the cap is a hard stop only for an unattended
        // actor. A human (User/ApiKey) hitting the cap does not get auto-denied by this
        // predicate — the same reasonCode/matchedCount are instead carried forward on an Allow
        // verdict (§9.4.1) so the fact remains observable evidence rather than silently
        // discarded, and the gate continues to predicates 4/5. The lineage rows themselves are
        // untouched and remain queryable evidence either way.
        if (actorKind is RecoveryActorKind.User or RecoveryActorKind.ApiKey)
        {
            return new EligibilityDecision(EligibilityVerdict.Allow, reasonCode, MatchedCount: matches.Count);
        }

        return new EligibilityDecision(EligibilityVerdict.Escalate, reasonCode, MatchedCount: matches.Count);
    }
}
