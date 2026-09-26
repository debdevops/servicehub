using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Replaying one dead letter, proposal first (unit 2.7). The one execution route: everything that ever
/// replays a single message — the drawer, approvals, agents — goes through <see cref="ReplayAsync"/>, which
/// is gated by the eligibility gate and recorded in the ledger before and after the cloud is touched.
/// </summary>
public interface IDlqReplayService
{
    /// <summary>The gate's decision for the caller acting on one dead letter. Read-only.</summary>
    Task<EligibilityDecision?> CheckEligibilityAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, RecoveryOperationKind kind, CancellationToken cancellationToken);

    /// <summary>What replaying would do. Read-only; nothing is executed.</summary>
    Task<Result<ReplayProposal>> ProposeAsync(long dlqMessageId, Namespace ns, RecoveryActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Replays it: gate, then ledger entry, then the cloud, then the outcome. Never retried. A gate refusal is
    /// a failure carrying the gate's reason code; a cloud refusal is a <see cref="ReplayOutcome"/> that says so.
    /// </summary>
    Task<Result<ReplayOutcome>> ReplayAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, string? intentHeader, string? correlationId,
        CancellationToken cancellationToken, long? ruleId = null);

    /// <summary>
    /// Purges it (unit 6.15): the same order as a replay — gate, ledger entry, cloud, outcome — and never retried. Only where
    /// the cloud can delete one message (<c>SupportsPurge</c>); a reason is required and recorded; automation may never purge.
    /// </summary>
    Task<Result<ReplayOutcome>> PurgeAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, string reason, string? intentHeader, string? correlationId,
        CancellationToken cancellationToken);

    /// <summary>The replays in the given namespaces, newest first — optionally only those of one dead letter.</summary>
    Task<ReplayPage> ListAsync(
        IReadOnlyCollection<Guid> namespaceIds, string? result, long? dlqMessageId, int page, int pageSize, CancellationToken cancellationToken);
}
