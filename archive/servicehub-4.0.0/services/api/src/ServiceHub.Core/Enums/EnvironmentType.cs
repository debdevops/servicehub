namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the deployment environment of a Service Bus namespace.
/// Controls safety guards and feature availability per namespace.
/// </summary>
public enum EnvironmentType
{
    /// <summary>Development environment — all features enabled, no restrictions.</summary>
    Dev = 0,

    /// <summary>User Acceptance Testing — test message generation disabled.</summary>
    Uat = 1,

    /// <summary>
    /// Production (ADR-0010 §Decision, phase 1 &amp; 2). Registrable and fully observable —
    /// Investigate, Correlate and Prevent operate against it without restriction. Every recovery
    /// verb (<c>MessagesController</c>, <c>BulkOperationExecutor</c>, <c>SignatureReplayExecutor</c>,
    /// <c>RulesController</c>'s replay-all) stays denied unless a live, two-person-approved
    /// <c>ProductionElevation</c> covers the exact namespace — predicate 2 of
    /// <c>RecoveryEligibilityGate</c> enforces this for a <c>User</c>/<c>ApiKey</c> actor.
    /// <c>DlqMonitorWorker</c>'s auto-replay rule scan denies it unconditionally regardless of any
    /// elevation: no <c>AutonomyGrant</c> is ever issued against a Prod namespace, and no
    /// autonomy ladder applies here, under any configuration (the M2.4 hard ceiling).
    /// </summary>
    Prod = 2
}
