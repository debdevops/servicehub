namespace ServiceHub.Core.Entities;

/// <summary>
/// One row per (namespace, shared-with-owner) pair — the durable form of
/// <see cref="Namespace.SharedWithOwnerIds"/>. The only table in the M2 persistence wave that gets
/// a real foreign key into <see cref="Namespace"/> (cascade delete): sharing metadata has no
/// evidentiary value, so deleting a namespace should delete who it was shared with.
/// </summary>
public sealed class NamespaceSharedOwner
{
    /// <summary>The namespace this share grant applies to.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The owner identity granted access.</summary>
    public required string OwnerId { get; init; }
}
