using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>A blob container that an event subscription dead-letters into.</summary>
/// <param name="Account">The storage account's name.</param>
/// <param name="Container">The container's name.</param>
/// <param name="AccountResourceId">The storage account's resource id, for a link to the portal, or null.</param>
internal sealed record DeadLetterContainer(string Account, string Container, string? AccountResourceId);

/// <summary>One dead-letter blob, from a container listing.</summary>
internal sealed record DeadLetterBlob(string Account, string Container, string Name, DateTimeOffset? ModifiedUtc, long? Length);

/// <summary>One dead-letter blob read for its reason. Never carries the event's data.</summary>
internal sealed record DeadLetterBlobRead(DeadLetterBlob Blob, string? Reason, string? Outcome, int? Attempts, int? HttpStatus, DateTimeOffset? LastAttemptUtc);

/// <summary>
/// The only way <see cref="EventGridSource"/> touches Azure: list subscriptions, list event subscriptions and their dead-letter
/// containers, list blobs, get one blob. Narrow on purpose, so a test can stand in for Azure.
/// </summary>
internal interface IAzureEventGridReader
{
    /// <summary>The subscription ids the connection's identity can see.</summary>
    Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct);

    /// <summary>The dead-letter containers of the event subscriptions in one Azure subscription (subscription scope, topics and system topics).</summary>
    Task<Listing<DeadLetterContainer>> ListDeadLetterContainersAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct);

    /// <summary>Blobs in one container, up to <paramref name="max"/>.</summary>
    Task<Listing<DeadLetterBlob>> ListBlobsAsync(Namespace ns, string account, string container, int max, CancellationToken ct);

    /// <summary>One blob's reason, or null when it is gone.</summary>
    Task<DeadLetterBlobRead?> GetBlobAsync(Namespace ns, string account, string container, string blobName, CancellationToken ct);
}
