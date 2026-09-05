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
    /// Production — connectivity is refused outright: <c>CreateNamespaceRequest.Validate</c>
    /// rejects registering a namespace at this level, and every mutating path denies it
    /// independently (<c>MessagesController</c>, <c>BulkOperationExecutor</c>,
    /// <c>SignatureReplayExecutor</c>, <c>DlqMonitorWorker</c>'s auto-replay rule scan, and
    /// predicate 2 of <c>RecoveryEligibilityGate</c>, which is unconditional because no
    /// elevation-recording mechanism exists). Replay is blocked, not merely confirmed.
    /// </summary>
    Prod = 2
}
