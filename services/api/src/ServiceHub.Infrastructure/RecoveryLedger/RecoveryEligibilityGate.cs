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

    private const string ReasonPurgeAutomationProhibited = "PURGE_AUTOMATION_PROHIBITED";
    private const string ReasonProductionElevationRequired = "PRODUCTION_ELEVATION_REQUIRED";
    private const string ReasonAmbiguousCollision = "RECURRENCE_CAP_AMBIGUOUS_COLLISION";
    private const string ReasonExceededExact = "RECURRENCE_CAP_EXCEEDED";
    private const string ReasonExceededHeuristic = "RECURRENCE_CAP_EXCEEDED_HEURISTIC";
    private const string ReasonQueryError = "RECURRENCE_CAP_QUERY_ERROR";

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
        if (request.NamespaceId is { } namespaceId
            && !string.IsNullOrEmpty(request.EntityNameSnapshot)
            && !string.IsNullOrEmpty(request.BodyHash))
        {
            var lineageDecision = await EvaluateRecurrenceLineageAsync(
                request.OwnerId, namespaceId, request.EntityNameSnapshot, request.BodyHash,
                request.ActorKind, cancellationToken);
            if (lineageDecision is not null)
            {
                return lineageDecision;
            }
        }

        // Predicate 4 — rate limit (§9: "wraps the existing CanReplayAsync"). Pre-computed by the
        // caller; false for every caller with no per-rule rate-limit concept.
        if (request.RateLimitExceeded)
        {
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonRateLimited);
        }

        // Predicate 5 — autonomy lookup (§9, soft-launch per §29.6): log-only until Phase D ships
        // AutonomyGrant. No grant infrastructure exists yet, so an Automation actor would
        // unconditionally escalate if this predicate were enforced — logged for visibility, never
        // returned as the gate's verdict this phase.
        if (request.ActorKind == RecoveryActorKind.Automation)
        {
            _logger.LogInformation(
                "Eligibility Gate predicate 5 (autonomy lookup) would escalate for owner {OwnerId} — " +
                "no AutonomyGrant exists yet (log-only until Phase D, roadmap §29.6); proceeding as Allow",
                request.OwnerId);
        }

        return EligibilityDecision.Allow;
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

        // Option B (accepted roadmap decision): the cap is a hard stop only for an unattended
        // actor. A human (User/ApiKey) hitting the cap does not get auto-denied by this
        // predicate — the gate continues to predicate 4/5, and if those pass, the human's
        // recovery proceeds and is recorded as its own real outcome, not a fabricated Declined
        // entry. The lineage rows themselves are untouched and remain queryable evidence either
        // way.
        if (actorKind is RecoveryActorKind.User or RecoveryActorKind.ApiKey)
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

        return new EligibilityDecision(EligibilityVerdict.Escalate, reasonCode, MatchedCount: matches.Count);
    }
}
