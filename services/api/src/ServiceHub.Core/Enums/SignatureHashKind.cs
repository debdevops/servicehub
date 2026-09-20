namespace ServiceHub.Core.Enums;

/// <summary>
/// Which of the two incompatible vocabularies a <see cref="Entities.NamespaceSignature.SignatureHash"/>
/// value belongs to (roadmap M1.4, ADR-0009 §Decision unit 2). Before this discriminator existed,
/// both vocabularies shared one column, so the table held roughly two rows per real failure with
/// occurrence counts split between them.
/// </summary>
public enum SignatureHashKind
{
    /// <summary>
    /// <c>FailureFingerprintBuilder</c>'s trust fingerprint — what Incidents, the attention queue
    /// and every <see cref="Entities.AutonomyGrant"/> key on. The safety-critical space: this
    /// value must never be recomputed or reassigned once a signature has been observed, because
    /// every earned autonomy grant is keyed on it.
    /// </summary>
    Fingerprint = 0,

    /// <summary>
    /// <c>ClusterSignatureHasher</c>'s cluster hash — what <c>GET /api/v1/.../dlq/signatures</c>
    /// keys on. Derived from the cluster's top distinguishing terms and dominant deadletter
    /// reason as currently observed; carries no autonomy trust of its own.
    /// </summary>
    Cluster = 1,
}
