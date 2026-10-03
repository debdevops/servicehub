using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IRecoveryRehearsalService"/>
/// </summary>
/// <remarks>
/// Deliberately depends on only <see cref="IRecoveryLedger"/> (read side) and
/// <see cref="IRecoveryEligibilityGate"/> — <see cref="IRecoveryEligibilityGate.EvaluateAsync"/>
/// itself makes no ledger writes, and this service never calls any of <c>IRecoveryLedger</c>'s
/// write methods, so there is no code path from here to <c>RecordDeclinedAsync</c>, a provider
/// call, or any other mutation. That is what "architecturally incapable of reaching a broker"
/// means in practice: not a configuration flag that could be flipped, but the absence of any
/// injected dependency capable of it.
/// </remarks>
public sealed class RecoveryRehearsalService : IRecoveryRehearsalService
{
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEligibilityGate _eligibilityGate;

    /// <summary>Initialises a new instance of <see cref="RecoveryRehearsalService"/>.</summary>
    public RecoveryRehearsalService(IRecoveryLedger recoveryLedger, IRecoveryEligibilityGate eligibilityGate)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _eligibilityGate = eligibilityGate ?? throw new ArgumentNullException(nameof(eligibilityGate));
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryRehearsalResult>> RehearseAsync(
        Guid entryId, string ownerId, RecoveryActorKind actorKind, CancellationToken cancellationToken = default)
    {
        var entry = await _recoveryLedger.GetEntryAsync(entryId, ownerId, cancellationToken);
        if (entry is null)
        {
            return Result<RecoveryRehearsalResult>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        // Every entry belongs to an operation by construction (BeginEntryAsync requires an open
        // operation) — a miss here means the two tables have drifted, not a normal not-found; fail
        // closed rather than guess an ActionKind/Trigger the gate needs.
        var operation = await _recoveryLedger.GetOperationAsync(entry.OperationId, ownerId, cancellationToken);
        if (operation is null)
        {
            return Result<RecoveryRehearsalResult>.Failure(Error.NotFound(
                "RecoveryLedger.OperationNotFound", "Parent recovery operation not found."));
        }

        var request = new RecoveryEligibilityRequest(
            OwnerId: ownerId,
            ActionKind: operation.Kind,
            ActorKind: actorKind,
            Trigger: operation.Trigger,
            NamespaceId: entry.NamespaceId,
            EntityNameSnapshot: entry.EntityNameSnapshot,
            BodyHash: entry.BodyHash,
            SignatureHash: entry.SignatureHashSnapshot,
            Environment: entry.EnvironmentSnapshot,
            Provider: entry.ProviderSnapshot);

        var decision = await _eligibilityGate.EvaluateAsync(request, cancellationToken);

        return Result<RecoveryRehearsalResult>.Success(new RecoveryRehearsalResult(
            entry.Id, actorKind, decision, DateTimeOffset.UtcNow));
    }
}
