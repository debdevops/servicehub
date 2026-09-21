using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Request to record a provider call's outcome against an entry in
/// <see cref="RecoveryEntryState.Executing"/>. See <c>IRecoveryLedger.RecordExecutionAsync</c>.</summary>
public sealed class RecordExecutionRequest
{
    /// <summary>The entry being recorded against.</summary>
    public required Guid EntryId { get; init; }

    /// <summary>Owner ID — must match the entry's owner.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The resolved actor recording this outcome.</summary>
    public required RecoveryActor Actor { get; init; }

    /// <summary>What the provider call returned.</summary>
    public required RecoveryExecutionOutcome Outcome { get; init; }

    /// <summary>Structured, non-payload detail about the provider's response (redacted before persist).</summary>
    public string? ProviderDetailJson { get; init; }

    /// <summary>The <c>x-servicehub-recovery-id</c> marker value, if one was stamped. Populated in a later phase.</summary>
    public string? RecoveryMarker { get; init; }

    /// <summary>Whether the marker was actually applied.</summary>
    public bool MarkerApplied { get; init; }
}
