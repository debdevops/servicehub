namespace ServiceHub.Core.Entities;

/// <summary>
/// Durable per-namespace liveness state for the infrastructure-attested DLQ observer
/// (`cloud-platform-infra` ADR-004; ADR-0011). Mutable, not hash-chained — this is
/// infrastructure-liveness telemetry, the same tier <see cref="NamespaceSignature"/> and
/// <see cref="AutonomyGrant"/> occupy, not a claim about what ServiceHub did.
/// </summary>
/// <remarks>
/// <see cref="IsLiveAt"/> is the single fact every consumer (the Recovery Eligibility Gate's
/// predicate 5, <c>AutonomyEvaluationWorker</c>'s promotion check) evaluates to decide whether a
/// namespace's <c>CanProveDlqAbsence</c> is overridden from the provider's static default to
/// <see langword="true"/>. Per ADR-004 item 4, that fact must never default true and must never
/// silently age: a namespace whose observer has never confirmed, or whose confirmation is older
/// than <see cref="StalenessBoundMinutes"/>, is judged not live — the same fail-closed posture
/// every other safety-relevant read in this codebase takes.
/// </remarks>
public sealed class DlqObserverAttestation
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-tenant isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The namespace this attestation covers — soft reference, no FK, matching every other
    /// ledger-adjacent NamespaceId in this schema. Unique per (OwnerId, NamespaceId).</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Whether attestation is active for this namespace. Opt-in: an operator sets this
    /// only once the <c>cloud-platform-infra</c> observer module has actually been applied for
    /// this namespace's DLQ. A worker never sweeps a namespace with this false, and
    /// <see cref="IsLiveAt"/> is unconditionally false when this is false.</summary>
    public bool Enabled { get; set; }

    /// <summary>The DynamoDB table name (AWS) or Firestore collection name (GCP) the observer's
    /// log lives in — operator-supplied, read from the corresponding Terraform module's own
    /// output (<c>table_name</c>/<c>firestore_collection_name</c>).</summary>
    public string? ObserverReference { get; set; }

    /// <summary>The DLQ entity name the liveness canary is sent to, via the existing
    /// provider-neutral <c>IMessageOperationsService.SendAsync</c>.</summary>
    public string? DlqEntityName { get; set; }

    /// <summary>When the most recently dispatched liveness canary was sent. Null before the first
    /// canary cycle.</summary>
    public DateTimeOffset? LastCanarySentAt { get; set; }

    /// <summary>The tracking ID stamped on the most recently dispatched canary's
    /// <c>ApplicationProperties</c>, used to look it up in the observer's log.</summary>
    public string? LastCanaryMessageId { get; set; }

    /// <summary>When the observer's log last confirmed a canary's arrival within
    /// <see cref="StalenessBoundMinutes"/> of being sent. Null means never confirmed —
    /// <see cref="IsLiveAt"/> is false regardless of <see cref="Enabled"/> until this is set at
    /// least once.</summary>
    public DateTimeOffset? LastConfirmedAt { get; set; }

    /// <summary>How old <see cref="LastConfirmedAt"/> may be before liveness is judged false.
    /// Never "assume fine" past this bound — see this entity's own remarks.</summary>
    public required int StalenessBoundMinutes { get; set; }

    /// <summary>
    /// Whether this namespace's observer is currently live: enabled, and confirmed within the
    /// staleness bound. The single predicate every consumer of this table evaluates.
    /// </summary>
    public bool IsLiveAt(DateTimeOffset asOf) =>
        Enabled
        && LastConfirmedAt is { } confirmedAt
        && asOf - confirmedAt <= TimeSpan.FromMinutes(StalenessBoundMinutes);
}
