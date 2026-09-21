namespace ServiceHub.Shared.Helpers;

/// <summary>
/// Classifies a delivery/receive count into a coarse band.
/// </summary>
/// <remarks>
/// Both of ServiceHub's signature identities — the trust fingerprint
/// (<c>FailureFingerprintBuilder</c>) and the DLQ-Intelligence cluster signature
/// (<c>DeterministicClusteringStrategy</c> feeding <see cref="ClusterSignatureHasher"/>) — hash a
/// set of distinguishing terms, and both want retry behaviour to be one of those terms.
///
/// <para>
/// Retry counts cannot be hashed raw. A cluster's mean delivery count drifts continuously as
/// messages enter and leave the DLQ, so hashing it re-identifies the same failure every time the
/// mean crosses an integer boundary — splitting its occurrence count, orphaning any operator
/// knowledge and lifecycle status keyed to the old hash, and listing one failure twice in the
/// attention queue. Observed live on 2026-09-05: one AWS failure recorded as <c>5e5177a9…</c>
/// (mean 10) and <c>d5c3e46a…</c> (mean 4).
/// </para>
///
/// <para>
/// The bands below are the ones the fingerprint builder has always used; this type exists so the
/// two producers cannot drift apart again. Changing a threshold changes signature identity for
/// every existing signature, so treat it as a versioned algorithm change, not a tweak.
/// </para>
/// </remarks>
public static class DeliveryCountBucket
{
    /// <summary>Band name for a failure that gives up almost immediately.</summary>
    public const string Low = "low";

    /// <summary>Band name for a failure that retries within a typical delivery limit.</summary>
    public const string Medium = "medium";

    /// <summary>Band name for a failure that exhausts an unusually long retry chain.</summary>
    public const string High = "high";

    /// <summary>
    /// Returns the band for <paramref name="deliveryCount"/>, or <see langword="null"/> when the
    /// count carries no signal (zero or negative — e.g. a provider that does not report one).
    /// </summary>
    public static string? Classify(int deliveryCount) => deliveryCount switch
    {
        <= 0 => null,
        <= 3 => Low,
        <= 10 => Medium,
        _ => High,
    };
}
