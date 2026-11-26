using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The one route that asks a cloud to redrive a failed run of a component that is not a message store (plan PP5): through the
/// eligibility gate, on the ledger before and after the cloud is touched, once, never retried. Only a person may use it.
/// </summary>
public interface IComponentRedriveService
{
    /// <summary>Whether the gate would allow this redrive now. Records nothing and calls no cloud.</summary>
    Task<Result<RedriveCheck>> CheckAsync(Namespace ns, string componentKey, string itemId, RecoveryActor actor, CancellationToken cancellationToken);

    /// <summary>Asks the cloud to redrive the run. The ledger entry ends <c>Unverified</c> when the cloud accepts it.</summary>
    Task<Result<RedriveOutcome>> RequestRedriveAsync(
        Namespace ns, string componentKey, string itemId, RecoveryActor actor, string? intentHeader, string? correlationId, CancellationToken cancellationToken);
}
