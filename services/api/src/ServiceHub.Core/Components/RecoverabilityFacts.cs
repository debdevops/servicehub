namespace ServiceHub.Core.Components;

/// <summary>
/// The three facts that describe what can be done for a failure on one component, plus whether a signal exists. The class is
/// calculated from these, so the facts and the class can never disagree.
/// </summary>
/// <param name="Copy">Is the failed work kept anywhere ServiceHub can read?</param>
/// <param name="Path">The safe way to put it back.</param>
/// <param name="Proof">The highest observation reachable for that one message.</param>
/// <param name="HasSignal">Whether a metric or state shows the problem when no copy is kept.</param>
public sealed record RecoverabilityFacts(CopyKind Copy, RecoveryPathKind Path, ProofLevel Proof, bool HasSignal = false)
{
    /// <summary>The safe default for anything not yet described: nothing kept, no path, no proof.</summary>
    public static readonly RecoverabilityFacts Unknown = new(CopyKind.None, RecoveryPathKind.None, ProofLevel.None);

    /// <summary>
    /// Derives the class. <paramref name="validated"/> is false until a recorded live run backs the facts; until then the
    /// answer is always <see cref="RecoverabilityClass.R0Unknown"/>, whatever the facts claim.
    /// </summary>
    public RecoverabilityClass Classify(bool validated)
    {
        if (!validated)
        {
            return RecoverabilityClass.R0Unknown;
        }

        if (Copy == CopyKind.Pointer)
        {
            return RecoverabilityClass.R4PointerOnly;
        }

        if (Path is RecoveryPathKind.Native or RecoveryPathKind.Resubmit)
        {
            return RecoverabilityClass.R3CloudRedriveOnly;
        }

        if (Copy == CopyKind.Held && Path == RecoveryPathKind.Adapter)
        {
            return Proof >= ProofLevel.NotReturned
                ? RecoverabilityClass.R1ReplayableProvable
                : RecoverabilityClass.R2ReplayableUnproven;
        }

        // A held copy with no proven path is still only something to look at.
        return HasSignal || Copy == CopyKind.Held
            ? RecoverabilityClass.R5DetectOnly
            : RecoverabilityClass.R6NotRecoverable;
    }
}

/// <summary>What each class allows. One place, so the gate, the API and the screens cannot disagree.</summary>
public static class RecoverabilityRules
{
    /// <summary>ServiceHub may replay one message itself (after the usual checks).</summary>
    public static bool OffersReplay(this RecoverabilityClass c) =>
        c is RecoverabilityClass.R1ReplayableProvable or RecoverabilityClass.R2ReplayableUnproven or RecoverabilityClass.R4PointerOnly;

    /// <summary>A person may approve the cloud's own redrive or re-run.</summary>
    public static bool OffersCloudRedrive(this RecoverabilityClass c) => c == RecoverabilityClass.R3CloudRedriveOnly;

    /// <summary>A result may read Verified, and a fix may earn the right to run without asking.</summary>
    public static bool MayEarnAutonomy(this RecoverabilityClass c) => c == RecoverabilityClass.R1ReplayableProvable;

    /// <summary>Nothing can be put back from here: show it and hand it to a person. No replay control exists.</summary>
    public static bool IsHandOffOnly(this RecoverabilityClass c) =>
        c is RecoverabilityClass.R0Unknown or RecoverabilityClass.R5DetectOnly or RecoverabilityClass.R6NotRecoverable;
}
