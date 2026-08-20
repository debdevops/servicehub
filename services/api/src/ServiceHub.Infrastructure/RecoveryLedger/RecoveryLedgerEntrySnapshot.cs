using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// Builds a <see cref="BeginRecoveryEntryRequest"/> from a tracked <see cref="DlqMessage"/> — the
/// same mapping every <see cref="DlqMessage"/>-driven recovery path
/// (<c>BulkOperationExecutor</c>, <c>SignatureReplayExecutor</c>, <c>AutoReplayExecutor</c>,
/// <c>RulesController.ReplayAll</c>) needs, centralised so it is expressed once.
/// </summary>
public static class RecoveryLedgerEntrySnapshot
{
    /// <summary>
    /// Maps a claimed <see cref="DlqMessage"/> and its <see cref="Namespace"/> onto a
    /// <see cref="BeginRecoveryEntryRequest"/>. <paramref name="signatureHashSnapshot"/> is
    /// supplied by the caller rather than read off <paramref name="message"/> — a message's
    /// failure signature isn't a stored column on <see cref="DlqMessage"/>, only known where the
    /// caller already has it on hand (e.g. <c>SignatureReplayJob.SignatureHash</c>).
    /// </summary>
    public static BeginRecoveryEntryRequest BuildBeginEntryRequest(
        DlqMessage message,
        Namespace ns,
        Guid operationId,
        string ownerId,
        RecoveryActor actor,
        string targetEntity,
        string? signatureHashSnapshot = null)
    {
        return new BeginRecoveryEntryRequest
        {
            OperationId = operationId,
            OwnerId = ownerId,
            Actor = actor,
            DlqMessageId = message.Id,
            NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.DisplayName ?? ns.Name,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            EntityNameSnapshot = message.EntityName,
            EntityTypeSnapshot = message.EntityType.ToString(),
            TopicNameSnapshot = message.TopicName,
            SourceMessageIdSnapshot = message.MessageId,
            SourceSequenceNumberSnapshot = message.SequenceNumber,
            BodyHash = message.BodyHash,
            FailureCategorySnapshot = message.FailureCategory,
            DeadLetterReasonSnapshot = message.DeadLetterReason,
            SignatureHashSnapshot = signatureHashSnapshot,
            TargetEntity = targetEntity,
        };
    }
}
