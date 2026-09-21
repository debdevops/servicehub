using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Request to open a new <see cref="RecoveryOperation"/>. See <c>IRecoveryLedger.OpenOperationAsync</c>.</summary>
public sealed class OpenRecoveryOperationRequest
{
    /// <summary>Owner ID the operation belongs to.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Whether this operation replays or purges its targets.</summary>
    public required RecoveryOperationKind Kind { get; init; }

    /// <summary>What caused this operation to open.</summary>
    public required RecoveryTrigger Trigger { get; init; }

    /// <summary>The resolved actor opening this operation.</summary>
    public required RecoveryActor Actor { get; init; }

    /// <summary>Operator-supplied justification. Required for <see cref="RecoveryOperationKind.Purge"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>The <c>X-ServiceHub-Intent</c> header value actually presented, if any.</summary>
    public string? IntentHeader { get; init; }

    /// <summary>The namespace this operation targets. Null for a cross-namespace operation.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Namespace display name, snapshotted at write time.</summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>Cloud provider snapshot.</summary>
    public CloudProviderType? ProviderSnapshot { get; init; }

    /// <summary>Deployment environment snapshot.</summary>
    public EnvironmentType? EnvironmentSnapshot { get; init; }

    /// <summary>Human-readable target scope, e.g. <c>entity=orders-dlq; category=Poison</c>.</summary>
    public required string ScopeDescription { get; init; }

    /// <summary>Provenance: the rule that fired this operation, if any.</summary>
    public long? SourceRuleId { get; init; }

    /// <summary>Provenance: the job that drove this operation, if any.</summary>
    public long? SourceJobId { get; init; }

    /// <summary>Correlation ID from the request pipeline.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Number of targets declared at open time.</summary>
    public required int TargetCount { get; init; }
}
