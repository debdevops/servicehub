using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Request to begin a new <see cref="RecoveryLedgerEntry"/> under an open operation.
/// See <c>IRecoveryLedger.BeginEntryAsync</c>.</summary>
public sealed class BeginRecoveryEntryRequest
{
    /// <summary>The operation this entry belongs to.</summary>
    public required Guid OperationId { get; init; }

    /// <summary>Owner ID — must match the operation's owner.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The resolved actor beginning this entry.</summary>
    public required RecoveryActor Actor { get; init; }

    /// <summary>Soft reference to the source message. No FK.</summary>
    public long? DlqMessageId { get; init; }

    /// <summary>The namespace this entry's message belongs to.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Namespace display name, snapshotted.</summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>Cloud provider snapshot.</summary>
    public CloudProviderType? ProviderSnapshot { get; init; }

    /// <summary>Deployment environment snapshot.</summary>
    public EnvironmentType? EnvironmentSnapshot { get; init; }

    /// <summary>The queue or subscription name the message was found in.</summary>
    public string? EntityNameSnapshot { get; init; }

    /// <summary>The entity type, snapshotted.</summary>
    public string? EntityTypeSnapshot { get; init; }

    /// <summary>Topic name, when the entity is a subscription.</summary>
    public string? TopicNameSnapshot { get; init; }

    /// <summary>The provider-assigned message ID before replay — record only.</summary>
    public string? SourceMessageIdSnapshot { get; init; }

    /// <summary>The sequence number before replay — record only.</summary>
    public long? SourceSequenceNumberSnapshot { get; init; }

    /// <summary>SHA-256 hash of the message body.</summary>
    public required string BodyHash { get; init; }

    /// <summary>Heuristic failure category, snapshotted.</summary>
    public FailureCategory? FailureCategorySnapshot { get; init; }

    /// <summary>Dead-letter reason, snapshotted.</summary>
    public string? DeadLetterReasonSnapshot { get; init; }

    /// <summary>Failure signature hash, snapshotted.</summary>
    public string? SignatureHashSnapshot { get; init; }

    /// <summary>The entity the message is being sent to or purged from.</summary>
    public required string TargetEntity { get; init; }
}
