using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Client for the optional, self-hosted reasoning-companion service (<c>services/agent</c>,
/// roadmap §7, W5). Structurally mirrors <see cref="IAIServiceClient"/>'s disabled-by-default,
/// never-throws-into-the-caller contract. This interface's only legal write-side consumer is
/// <c>ReasoningCompanionWorker</c>, which turns every returned <see cref="ReasoningProposal"/>
/// into an <see cref="IPlaybookLedger.ProposeAsync"/> call and nothing else — see
/// <c>AIBoundaryArchitectureTests</c>, extended to enforce that no type reachable from this
/// interface ever calls a mutating <see cref="IRecoveryLedger"/>/<see cref="IMessageOperationsService"/>
/// member, or any <see cref="IPlaybookLedger"/> member other than <c>ProposeAsync</c>.
/// </summary>
public interface IReasoningAgentClient
{
    /// <summary>
    /// Sends a batch of already-aggregated, payload-free evidence records and returns whatever
    /// advisory proposals the companion produced. Every failure path (disabled, unreachable,
    /// timeout, malformed response) degrades to an empty list rather than throwing or failing the
    /// caller — a reasoning companion that cannot currently reason is not an error condition.
    /// </summary>
    Task<Result<IReadOnlyList<ReasoningProposal>>> ProposeAsync(
        IReadOnlyList<ReasoningEvidenceRecord> evidence,
        CancellationToken cancellationToken = default);

    /// <summary>Whether the reasoning-companion service is currently reachable and configured
    /// with a local reasoning backend. <see langword="false"/> (never a failure) when disabled.</summary>
    Task<Result<bool>> IsAvailableAsync(CancellationToken cancellationToken = default);
}
