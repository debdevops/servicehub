using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// The input to <see cref="Interfaces.IRecoveryEligibilityGate.EvaluateAsync"/> — everything the
/// gate's deterministic predicates (roadmap §9) need to decide one recovery attempt. Every
/// caller — human, automation, any future AI-originated recommendation — builds the same shape
/// and passes through the same gate instance.
/// </summary>
/// <param name="OwnerId">Tenant scope for the recurrence-lineage lookup (predicate 3).</param>
/// <param name="ActionKind">Replay or Purge — drives predicate 1 (purge-origin prohibition).</param>
/// <param name="ActorKind">Who is attempting this — drives predicates 1 and 5.</param>
/// <param name="Trigger">What caused this attempt — carried through for ledger evidence, not
/// itself a predicate input.</param>
/// <param name="NamespaceId">Target namespace, for predicate 3's lineage scope.</param>
/// <param name="EntityNameSnapshot">Target entity, for predicate 3's lineage key.</param>
/// <param name="BodyHash">Message body hash, for predicate 3's lineage key.</param>
/// <param name="SignatureHash">Failure-signature hash, for predicate 5's autonomy lookup —
/// <see langword="null"/> means predicate 5 escalates unconditionally for <c>Automation</c>.</param>
/// <param name="Environment">Target namespace's environment — drives predicate 2 (production
/// elevation).</param>
/// <param name="RateLimitExceeded">Predicate 4's input, pre-computed by the caller via the
/// existing <c>IAutoReplayExecutor.CanReplayAsync</c> where a per-rule rate limit applies —
/// <see langword="false"/> (the default) for every caller with no rate-limit concept.</param>
/// <param name="FleetRateLimitExceeded">Predicate 4's fleet-wide input, pre-computed by the
/// caller via <c>IAutoReplayExecutor.CanReplayFleetWideAsync</c> — the owner's combined
/// automated-replay rate across every rule, not just this one. <see langword="false"/> (the
/// default) for every caller with no fleet-rate concept.</param>
/// <param name="Provider">Target namespace's cloud provider — drives predicate 5's independent
/// capability check: an <c>Automation</c> actor may only execute unattended
/// (<see cref="AutonomyLevel.Standing"/>/<see cref="AutonomyLevel.Unattended"/>) when the
/// provider's own <see cref="ProviderCapabilities.CanProveDlqAbsence"/> corroborates the grant,
/// so the gate never has to trust <c>AutonomyGrant.CurrentLevel</c> alone. <see langword="null"/>
/// is treated the same as an unresolvable provider — fails closed.</param>
public sealed record RecoveryEligibilityRequest(
    string OwnerId,
    RecoveryOperationKind ActionKind,
    RecoveryActorKind ActorKind,
    RecoveryTrigger Trigger,
    Guid? NamespaceId,
    string? EntityNameSnapshot,
    string? BodyHash,
    string? SignatureHash,
    EnvironmentType? Environment,
    bool RateLimitExceeded = false,
    CloudProviderType? Provider = null,
    bool FleetRateLimitExceeded = false);
