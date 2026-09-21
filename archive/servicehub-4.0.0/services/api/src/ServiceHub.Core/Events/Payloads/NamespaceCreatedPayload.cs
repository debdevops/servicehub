namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.NamespaceCreated"/>.
/// Carries the details of the newly created namespace configuration.
/// </summary>
public sealed record NamespaceCreatedPayload
{
    /// <summary>Unique identifier assigned to the new namespace.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Fully qualified namespace name (e.g. mynamespace.servicebus.windows.net).</summary>
    public required string NamespaceName { get; init; }

    /// <summary>Optional human-readable display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Cloud provider string (e.g. "azure", "aws", "gcp").</summary>
    public required string CloudProvider { get; init; }

    /// <summary>Authentication type used (e.g. "ConnectionString", "ManagedIdentity").</summary>
    public required string AuthType { get; init; }

    /// <summary>Owner identifier of the user who created the namespace.</summary>
    public required string OwnerId { get; init; }
}
