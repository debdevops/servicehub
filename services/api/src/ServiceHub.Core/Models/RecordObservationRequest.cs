using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Request to record a DLQ-scan observation against an entry in
/// <see cref="RecoveryEntryState.Observing"/>. See <c>IRecoveryLedger.RecordObservationAsync</c>.</summary>
public sealed class RecordObservationRequest
{
    /// <summary>The entry being observed.</summary>
    public required Guid EntryId { get; init; }

    /// <summary>Owner ID — must match the entry's owner.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The resolved actor recording this observation (typically <see cref="RecoveryActorKind.System"/>
    /// for the verification worker).</summary>
    public required RecoveryActor Actor { get; init; }

    /// <summary>What the scan found, or why it couldn't determine absence.</summary>
    public required RecoveryObservationOutcome Outcome { get; init; }

    /// <summary>How a <see cref="RecoveryObservationOutcome.RecurrenceObserved"/> match was attributed.
    /// Required when <see cref="Outcome"/> is <see cref="RecoveryObservationOutcome.RecurrenceObserved"/>.</summary>
    public VerificationConfidence? Confidence { get; init; }

    /// <summary>Structured, non-payload detail about the observation (redacted before persist) —
    /// e.g. collision count for a heuristic match, or the coverage-gap reason for an unavailable one.</summary>
    public string? DetailJson { get; init; }
}
