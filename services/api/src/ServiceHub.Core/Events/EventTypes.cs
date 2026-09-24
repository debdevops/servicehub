namespace ServiceHub.Core.Events;

/// <summary>
/// Canonical dotted-name constants for every <see cref="PlatformEvent"/> type
/// published by ServiceHub.
/// <para>
/// Convention: <c>servicehub.{category}.{verb}.{version}</c>
/// </para>
/// <para>
/// Rules:
/// <list type="bullet">
///   <item>All lowercase.</item>
///   <item>Dots as segment separators.</item>
///   <item>Past-tense verb — events record facts, not commands.</item>
///   <item>Explicit version suffix — consumers can filter on prefix and ignore newer versions.</item>
/// </list>
/// </para>
/// </summary>
public static class EventTypes
{
    // ── Namespace ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after a new namespace configuration is successfully persisted.
    /// </summary>
    public const string NamespaceCreated = "servicehub.namespace.created.v1";

    /// <summary>
    /// Raised after a namespace configuration is successfully removed.
    /// </summary>
    public const string NamespaceDeleted = "servicehub.namespace.deleted.v1";

    // ── DLQ ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the DLQ monitor detects one or more new messages in a dead-letter queue.
    /// </summary>
    public const string DlqMessageDetected = "servicehub.dlq.message.detected.v1";

    /// <summary>
    /// Raised when the DLQ monitor detects a volume spike above the configured threshold.
    /// </summary>
    public const string DlqSpikeDetected = "servicehub.dlq.spike.detected.v1";

    // ── Replay ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after a DLQ message replay attempt completes (success or failure).
    /// </summary>
    public const string ReplayCompleted = "servicehub.replay.completed.v1";

    // ── Rule ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when an auto-replay rule matches a DLQ message and an action is taken.
    /// </summary>
    public const string RuleMatched = "servicehub.rule.matched.v1";

    /// <summary>
    /// Raised when the success-rate circuit breaker automatically disables an auto-replay rule.
    /// </summary>
    public const string AutoReplayRuleCircuitBreakerTripped = "servicehub.rule.circuitbreaker.tripped.v1";

    // ── Autonomy ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when a failure signature's <c>AutonomyGrant</c> transitions to a new level — a
    /// promotion (evidence earned more standing trust) or a demotion (a verified-outcome floor
    /// breach or a duplicate-business-effect flag withdrew it).
    /// </summary>
    public const string AutonomyGrantTransitioned = "servicehub.autonomy.grant.transitioned.v1";

    // ── Bulk Operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Raised when a bulk replay/purge job reaches a terminal status
    /// (Completed, CompletedWithErrors, Failed, or Cancelled).
    /// </summary>
    public const string BulkOperationCompleted = "servicehub.bulkoperation.completed.v1";

    // ── Insight (Investigate / Prevent / Correlate) ──────────────────────────────

    /// <summary>
    /// Raised when a detection worker (anomaly, drift, correlation, or narration) produces a
    /// finding at or above its own significance threshold — the "push, don't wait to be asked"
    /// half of roadmap §5, I5. See <see cref="ServiceHub.Core.Enums.InsightKind"/> for which
    /// pillar produced it.
    /// </summary>
    public const string InsightDetected = "servicehub.insight.detected.v1";
}
