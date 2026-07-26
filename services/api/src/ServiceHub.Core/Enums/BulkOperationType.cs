namespace ServiceHub.Core.Enums;

/// <summary>
/// The mutating action a <see cref="Entities.BulkOperationJob"/> performs against every
/// matched DLQ message. Deliberately limited to the two operations
/// <see cref="Interfaces.IMessageOperationsService"/> already exposes per-message
/// (<c>ReplayMessageAsync</c> / <c>PurgeMessageAsync</c>) — a bulk job is an orchestration
/// layer over existing provider-neutral operations, not a new provider capability.
/// </summary>
public enum BulkOperationType
{
    /// <summary>Replays every matched message from the dead-letter queue back to its source entity.</summary>
    Replay = 0,

    /// <summary>Permanently deletes every matched message. Skipped per-message on providers where <see cref="Models.ProviderCapabilities.SupportsPurge"/> is false.</summary>
    Purge = 1
}
