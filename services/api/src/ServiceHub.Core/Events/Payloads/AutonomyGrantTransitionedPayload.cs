using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.AutonomyGrantTransitioned"/>.
/// Raised whenever <c>AutonomyEvaluationWorker</c> writes a promotion or demotion to
/// <c>AutonomyGrant</c> for a failure signature.
/// </summary>
public sealed record AutonomyGrantTransitionedPayload
{
    /// <summary>Owner whose signature's grant transitioned.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The failure signature whose grant transitioned.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>The recovery action kind the grant applies to (e.g. Replay).</summary>
    public required RecoveryOperationKind OperationKind { get; init; }

    /// <summary>The level before this transition.</summary>
    public required AutonomyLevel PreviousLevel { get; init; }

    /// <summary>The level after this transition.</summary>
    public required AutonomyLevel NewLevel { get; init; }

    /// <summary>Human-readable reason, matching the one written to the Recovery Ledger.</summary>
    public required string Reason { get; init; }

    /// <summary>UTC timestamp when the transition was recorded.</summary>
    public required DateTimeOffset TransitionedAtUtc { get; init; }
}
