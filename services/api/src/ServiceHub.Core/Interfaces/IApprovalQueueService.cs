using ServiceHub.Core.DTOs.Responses;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Reads the Approval Queue (roadmap §11 item 1): auto-replay rule matches the
/// <see cref="IRecoveryEligibilityGate"/> escalated for manual review (<c>RecoveryLedgerEntry</c>
/// rows in the <c>Declined</c> state, opened under an <c>AutoRule</c>-triggered operation), whose
/// underlying DLQ message is still <c>Active</c> — i.e. still eligible for a human to approve via
/// the existing single-message replay path. This is a read-only view; approval itself is just an
/// operator calling the existing replay endpoint, so there is no corresponding write method here.
/// </summary>
public interface IApprovalQueueService
{
    /// <summary>
    /// Lists pending approvals for <paramref name="ownerId"/>, most recently escalated first.
    /// </summary>
    /// <param name="ownerId">Tenant-isolation filter — only this owner's entries are returned.</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ApprovalQueueEntryResponse>> GetPendingApprovalsAsync(
        string ownerId, Guid? namespaceId, int limit, CancellationToken cancellationToken = default);
}
