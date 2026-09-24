using System.Diagnostics.Metrics;

namespace ServiceHub.Infrastructure.Telemetry;

/// <summary>
/// Domain (business) metrics for ServiceHub operations, built on the OpenTelemetry foundation.
/// These complement the automatic ASP.NET/HTTP instrumentation with operations-platform SLIs —
/// e.g. how large the dead-letter backlog is across the fleet, and how the recovery/safety
/// subsystem is behaving over time.
/// <para>
/// The underlying <see cref="Meter"/> always exists; it is only <i>exported</i> when
/// OpenTelemetry is enabled (see <c>ObservabilityExtensions</c> in <c>ServiceHub.Api</c>). When
/// it is not, recording is a cheap no-op, so this imposes no runtime cost on deployments that
/// have not opted in.
/// </para>
/// <para>
/// Lives in <c>ServiceHub.Infrastructure</c> rather than <c>ServiceHub.Api</c> because most of
/// its recording call sites are background workers and services in this layer
/// (<c>RecoveryEligibilityGate</c>, <c>AutonomyEvaluationWorker</c>,
/// <c>RecoveryVerificationWorker</c>) that cannot depend on the Api layer under Clean
/// Architecture's inward-only dependency rule. <c>ServiceHub.Api</c> still consumes it
/// (<c>FleetController</c>) since Api depends on Infrastructure.
/// </para>
/// <para>
/// Deliberately few instruments, each with low-cardinality tags only (bounded enum values —
/// verdict, reason code, disposition, autonomy level — never message IDs or signature hashes),
/// per the same no-message-content discipline the rest of the product's telemetry follows.
/// </para>
/// </summary>
public sealed class ServiceHubMetrics : IDisposable
{
    /// <summary>The meter name to register with the OpenTelemetry metrics pipeline.</summary>
    public const string MeterName = "ServiceHub.Operations";

    private readonly Meter _meter;
    private readonly Counter<long> _fleetOverviewRequests;
    private readonly Histogram<int> _fleetActiveBacklog;
    private readonly Counter<long> _eligibilityDecisions;
    private readonly Counter<long> _circuitBreakerTrips;
    private readonly Counter<long> _autonomyTransitions;
    private readonly Counter<long> _verificationOutcomes;

    /// <summary>Initializes a new instance of the <see cref="ServiceHubMetrics"/> class.</summary>
    public ServiceHubMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _fleetOverviewRequests = _meter.CreateCounter<long>(
            "servicehub.fleet.overview.requests",
            unit: "{request}",
            description: "Number of fleet overview requests served.");

        _fleetActiveBacklog = _meter.CreateHistogram<int>(
            "servicehub.fleet.active_backlog",
            unit: "{message}",
            description: "Active dead-letter backlog across the fleet at query time.");

        _eligibilityDecisions = _meter.CreateCounter<long>(
            "servicehub.recovery.eligibility.decisions",
            unit: "{decision}",
            description: "Recovery Eligibility Gate decisions, tagged by verdict and (when denied/escalated) reason code. Covers emergency stop, purge prohibition, production elevation, recurrence cap, per-rule and fleet-wide rate limiting, and autonomy-grant escalation — every predicate in the gate.");

        _circuitBreakerTrips = _meter.CreateCounter<long>(
            "servicehub.recovery.circuitbreaker.trips",
            unit: "{trip}",
            description: "Auto-replay rules disabled by the success-rate circuit breaker.");

        _autonomyTransitions = _meter.CreateCounter<long>(
            "servicehub.recovery.autonomy.transitions",
            unit: "{transition}",
            description: "AutonomyGrant level transitions written by the Autonomy Evaluation Worker, tagged by direction and the from/to levels.");

        _verificationOutcomes = _meter.CreateCounter<long>(
            "servicehub.recovery.verification.outcomes",
            unit: "{outcome}",
            description: "Recovery ledger entries closed by the Recovery Verification Worker, tagged by outcome and (when unverified) the limitation reason code.");
    }

    /// <summary>Records that a fleet overview was produced, with its headline figures.</summary>
    /// <param name="totalActive">Total active dead-letters across the fleet.</param>
    /// <param name="namespaceCount">Number of namespaces in the fleet.</param>
    public void RecordFleetOverview(int totalActive, int namespaceCount)
    {
        _fleetOverviewRequests.Add(1);
        _fleetActiveBacklog.Record(
            totalActive,
            new KeyValuePair<string, object?>("namespace.count", namespaceCount));
    }

    /// <summary>Records one <c>RecoveryEligibilityGate.EvaluateAsync</c> decision.</summary>
    /// <param name="verdict">The decision's <c>EligibilityVerdict</c> (e.g. "Allow", "Deny", "Escalate").</param>
    /// <param name="reason">The predicate's reason code when not a plain allow; <see langword="null"/> for a clean allow.</param>
    public void RecordEligibilityDecision(string verdict, string? reason)
    {
        _eligibilityDecisions.Add(1,
            new KeyValuePair<string, object?>("verdict", verdict),
            new KeyValuePair<string, object?>("reason", reason ?? "none"));
    }

    /// <summary>Records one auto-replay rule disabled by the success-rate circuit breaker.</summary>
    public void RecordCircuitBreakerTrip() => _circuitBreakerTrips.Add(1);

    /// <summary>Records one <c>AutonomyGrant</c> level transition.</summary>
    /// <param name="direction">"promotion" or "demotion".</param>
    /// <param name="from">The prior <c>AutonomyLevel</c>.</param>
    /// <param name="to">The new <c>AutonomyLevel</c>.</param>
    public void RecordAutonomyTransition(string direction, string from, string to)
    {
        _autonomyTransitions.Add(1,
            new KeyValuePair<string, object?>("direction", direction),
            new KeyValuePair<string, object?>("from", from),
            new KeyValuePair<string, object?>("to", to));
    }

    /// <summary>Records one recovery ledger entry closed by the Recovery Verification Worker.</summary>
    /// <param name="outcome">The <c>RecoveryObservationOutcome</c> the entry closed with.</param>
    /// <param name="reason">The limitation reason code when coverage was unavailable; <see langword="null"/> otherwise.</param>
    public void RecordVerificationOutcome(string outcome, string? reason)
    {
        _verificationOutcomes.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("reason", reason ?? "none"));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
