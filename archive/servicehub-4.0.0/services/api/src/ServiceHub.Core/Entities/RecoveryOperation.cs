using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One row per operator or automation *decision* to replay or purge, regardless of how many
/// messages it targets — the immutable header of the Recovery Evidence Ledger. Fully immutable:
/// no field is ever updated after insert, and the append-only guard in
/// <c>DlqDbContext.SaveChangesAsync</c> enforces that at the persistence layer.
/// </summary>
/// <remarks>
/// Per-message evidence lives on the paired <see cref="RecoveryLedgerEntry"/> rows (one per
/// target message) and the hash-chained <see cref="RecoveryEvent"/> rows. No <c>CompletedAt</c>
/// or counters are stored here — those are aggregates over entries, computed on read, which is
/// exactly what keeps this header 100% immutable.
/// </remarks>
public sealed class RecoveryOperation
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Whether this operation replays or purges its targets.</summary>
    public required RecoveryOperationKind Kind { get; init; }

    /// <summary>What caused this operation to open.</summary>
    public required RecoveryTrigger Trigger { get; init; }

    /// <summary>
    /// Server-derived actor identity — never a caller-supplied string. See
    /// <see cref="Models.RecoveryActor"/> and <see cref="Interfaces.IRecoveryLedger"/>.
    /// </summary>
    public required string ActorIdentity { get; init; }

    /// <summary>The category of actor behind this operation.</summary>
    public required RecoveryActorKind ActorKind { get; init; }

    /// <summary>Granted scopes at decision time, for API key actors.</summary>
    public string? ActorScopes { get; init; }

    /// <summary>Operator-supplied justification. Required for <see cref="RecoveryOperationKind.Purge"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>The <c>X-ServiceHub-Intent</c> header value actually presented, if any.</summary>
    public string? IntentHeader { get; init; }

    /// <summary>The namespace this operation targeted. Null for a cross-namespace operation.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>
    /// Namespace display name, snapshotted at write time — survives namespace deletion. Same
    /// rationale as <see cref="AuditLog.NamespaceName"/> and <see cref="BulkOperationJob.NamespaceDisplayName"/>.
    /// </summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>Cloud provider snapshot.</summary>
    public CloudProviderType? ProviderSnapshot { get; init; }

    /// <summary>Deployment environment snapshot.</summary>
    public EnvironmentType? EnvironmentSnapshot { get; init; }

    /// <summary>Human-readable target scope, e.g. <c>entity=orders-dlq; category=Poison</c>.</summary>
    public required string ScopeDescription { get; init; }

    /// <summary>Provenance: the <see cref="AutoReplayRule"/> that fired this operation, if any. No FK.</summary>
    public long? SourceRuleId { get; init; }

    /// <summary>Provenance: the <see cref="BulkOperationJob"/> or <see cref="SignatureReplayJob"/> that
    /// drove this operation, if any. No FK.</summary>
    public long? SourceJobId { get; init; }

    /// <summary>Correlation ID from the request pipeline, for cross-referencing logs.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Assembly version of ServiceHub at write time.</summary>
    public required string ServiceVersion { get; init; }

    /// <summary>When this operation was opened.</summary>
    public required DateTimeOffset OpenedAt { get; init; }

    /// <summary>Number of targets declared at open time.</summary>
    public required int TargetCount { get; init; }
}
