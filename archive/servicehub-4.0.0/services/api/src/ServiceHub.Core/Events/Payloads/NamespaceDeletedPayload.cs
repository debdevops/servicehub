namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.NamespaceDeleted"/>.
/// Carries a snapshot of the namespace at the time of deletion.
/// Snapshotted values allow subscribers (e.g. audit, analytics) to reference
/// the namespace even after it has been removed from the repository.
/// </summary>
public sealed record NamespaceDeletedPayload
{
    /// <summary>Unique identifier of the deleted namespace.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>
    /// Fully qualified namespace name at the time of deletion.
    /// Snapshotted so subscribers can display the name without a live lookup.
    /// </summary>
    public required string NamespaceName { get; init; }

    /// <summary>Cloud provider string (e.g. "azure", "aws", "gcp").</summary>
    public required string CloudProvider { get; init; }

    /// <summary>Owner identifier of the user who deleted the namespace.</summary>
    public required string OwnerId { get; init; }
}
