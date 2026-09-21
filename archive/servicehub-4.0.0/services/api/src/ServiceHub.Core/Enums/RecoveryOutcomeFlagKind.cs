namespace ServiceHub.Core.Enums;

/// <summary>
/// The kind of operator attestation carried by a <see cref="RecoveryEventType.OutcomeFlagged"/>
/// event (roadmap §8.10, §9.3). Both values are human-attested only — <c>ActorKind</c> on the
/// flagging event must be <c>User</c> or <c>ApiKey</c>, never system- or AI-inferred, since
/// ServiceHub cannot observe business-level harm directly.
/// </summary>
public enum RecoveryOutcomeFlagKind
{
    /// <summary>The operator judges this entry's recovery to have caused real business harm.
    /// Disqualifies the owner's entire fleet from L4/L5 (§8.10's <c>unsafe_outcome_count</c>
    /// gate is fleet-level, not per-signature) until resolved.</summary>
    Unsafe = 0,

    /// <summary>The operator judges this entry's recovery to have produced a duplicate
    /// business-level effect. A permanent, per-signature disqualifier (§8.10's
    /// <c>duplicate_association</c>) — not restorable without a separate, explicit human act.</summary>
    DuplicateBusinessEffect = 1
}
