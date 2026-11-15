using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>A poison queue: a Storage queue whose name ends in <c>-poison</c>, the convention Azure Functions and WebJobs move failing messages to.</summary>
/// <param name="Account">The storage account's name.</param>
/// <param name="Queue">The queue's name.</param>
/// <param name="AccountResourceId">The storage account's resource id, for a link to the portal, or null.</param>
internal sealed record PoisonQueue(string Account, string Queue, string? AccountResourceId);

/// <summary>One message seen by a peek. The message text is never carried: it may hold anything the application put there.</summary>
internal sealed record PoisonMessage(string Account, string Queue, string MessageId, DateTimeOffset? InsertedUtc, int? DequeueCount);

/// <summary>
/// The only way <see cref="StorageQueuesSource"/> touches Azure: list subscriptions, list storage accounts and their poison queues, and
/// peek messages. A peek neither hides nor removes a message and does not raise its dequeue count.
/// </summary>
internal interface IAzureStorageQueuesReader
{
    /// <summary>The subscription ids the connection's identity can see.</summary>
    Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct);

    /// <summary>The poison queues in the storage accounts of one Azure subscription.</summary>
    Task<Listing<PoisonQueue>> ListPoisonQueuesAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct);

    /// <summary>The oldest messages of one queue (a peek), up to <paramref name="max"/>.</summary>
    Task<Listing<PoisonMessage>> ListMessagesAsync(Namespace ns, string account, string queue, int max, CancellationToken ct);
}
