using Google.Cloud.Eventarc.V1;
using Google.Cloud.Location;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Google's side of <see cref="IGcpEventarcReader"/>. It calls <c>ListLocations</c>, <c>ListTriggers</c>, <c>GetTrigger</c>,
/// <c>GetSubscription</c>, <c>ListTopicSubscriptions</c>, and <c>Pull</c> followed by <c>ModifyAckDeadline(0)</c>. It never
/// acknowledges, so nothing is deleted, and it never publishes.
/// </summary>
internal sealed class GcpEventarcReader : IGcpEventarcReader
{
    /// <summary>The most pulls made in one look at a topic. A released message can be handed out again, so a look stops when a pull brings nothing new.</summary>
    internal const int MaxPulls = 15;

    private const int PullBatch = 10;
    private const string ReaderKey = "eventarc-read";

    private readonly IGcpClientFactory _clients;

    public GcpEventarcReader(IGcpClientFactory clients) =>
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public async Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct)
    {
        var client = await _clients.GetEventarcClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client.LocationsClient
            .ListLocationsAsync(new ListLocationsRequest { Name = $"projects/{ProjectOf(ns)}" })
            .ReadPageAsync(100, ct).ConfigureAwait(false);
        return new Listing<string>([.. page.Select(l => l.LocationId)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<Listing<TriggerRef>> ListTriggersAsync(Namespace ns, string location, int max, CancellationToken ct)
    {
        var client = await _clients.GetEventarcClientAsync(ns, ct).ConfigureAwait(false);
        var page = await client
            .ListTriggersAsync(new ListTriggersRequest { Parent = $"projects/{ProjectOf(ns)}/locations/{location}", PageSize = max })
            .ReadPageAsync(max, ct).ConfigureAwait(false);
        return new Listing<TriggerRef>([.. page.Select(Map)], !string.IsNullOrEmpty(page.NextPageToken));
    }

    public async Task<TriggerRef?> GetTriggerAsync(Namespace ns, string triggerName, CancellationToken ct)
    {
        var client = await _clients.GetEventarcClientAsync(ns, ct).ConfigureAwait(false);
        try
        {
            return Map(await client.GetTriggerAsync(new GetTriggerRequest { Name = triggerName }, ct).ConfigureAwait(false));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> GetDeadLetterTopicAsync(Namespace ns, string subscriptionName, CancellationToken ct)
    {
        var subscriber = await _clients.GetSubscriberClientAsync(ns, ReaderKey, ct).ConfigureAwait(false);
        try
        {
            var subscription = await subscriber.GetSubscriptionAsync(new GetSubscriptionRequest { Subscription = subscriptionName }, ct).ConfigureAwait(false);
            return string.IsNullOrEmpty(subscription.DeadLetterPolicy?.DeadLetterTopic) ? null : subscription.DeadLetterPolicy.DeadLetterTopic;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<DeadLetterRead> ListUndeliveredAsync(Namespace ns, string deadLetterTopic, int max, CancellationToken ct)
    {
        var subscription = await PullSubscriptionAsync(ns, deadLetterTopic, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return new DeadLetterRead([], false, false);
        }

        var subscriber = await _clients.GetSubscriberClientAsync(ns, ReaderKey, ct).ConfigureAwait(false);
        var seen = new Dictionary<string, UndeliveredEvent>(StringComparer.Ordinal);
        for (var pull = 0; pull < MaxPulls && seen.Count < max; pull++)
        {
            var batch = await PullAndReleaseAsync(subscriber, subscription, ct).ConfigureAwait(false);
            var added = 0;
            foreach (var message in batch)
            {
                if (seen.Count < max && seen.TryAdd(message.MessageId, message))
                {
                    added++;
                }
            }

            if (added == 0)
            {
                break;
            }
        }

        return new DeadLetterRead([.. seen.Values], seen.Count >= max, true);
    }

    public async Task<UndeliveredEvent?> GetUndeliveredAsync(Namespace ns, string deadLetterTopic, string messageId, CancellationToken ct)
    {
        var subscription = await PullSubscriptionAsync(ns, deadLetterTopic, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return null;
        }

        var subscriber = await _clients.GetSubscriberClientAsync(ns, ReaderKey, ct).ConfigureAwait(false);
        for (var pull = 0; pull < MaxPulls; pull++)
        {
            var batch = await PullAndReleaseAsync(subscriber, subscription, ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            if (batch.FirstOrDefault(m => m.MessageId == messageId) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The first subscription on the topic that is pulled (has no push endpoint), or null when there is none to look through.</summary>
    private async Task<string?> PullSubscriptionAsync(Namespace ns, string deadLetterTopic, CancellationToken ct)
    {
        var admin = await _clients.GetTopicAdminClientAsync(ns, ct).ConfigureAwait(false);
        var subscriber = await _clients.GetSubscriberClientAsync(ns, ReaderKey, ct).ConfigureAwait(false);
        var page = await admin.ListTopicSubscriptionsAsync(new ListTopicSubscriptionsRequest { Topic = deadLetterTopic, PageSize = 20 })
            .ReadPageAsync(20, ct).ConfigureAwait(false);
        foreach (var name in page)
        {
            var subscription = await subscriber.GetSubscriptionAsync(new GetSubscriptionRequest { Subscription = name }, ct).ConfigureAwait(false);
            if (subscription.PushConfig is null || string.IsNullOrEmpty(subscription.PushConfig.PushEndpoint))
            {
                return name;
            }
        }

        return null;
    }

    private static async Task<List<UndeliveredEvent>> PullAndReleaseAsync(SubscriberServiceApiClient subscriber, string subscription, CancellationToken ct)
    {
        // A pull that waits for a message blocks until one arrives, so on an empty dead-letter subscription (the good case) the look ran out
        // its whole time budget and was reported as a failed read (first live run). ReturnImmediately asks "is anything there now?", as the
        // observer check does; Pub/Sub may answer empty while messages exist, so an empty answer is asked once more before it is believed.
        PullResponse response = new();
        for (var attempt = 0; attempt < 2 && response.ReceivedMessages.Count == 0; attempt++)
        {
#pragma warning disable CS0612, CS0618
            response = await subscriber.PullAsync(new PullRequest { Subscription = subscription, MaxMessages = PullBatch, ReturnImmediately = true }, ct).ConfigureAwait(false);
#pragma warning restore CS0612, CS0618
        }

        if (response.ReceivedMessages.Count == 0)
        {
            return [];
        }

        // Released at once (deadline zero): the message is deliverable again straight away, and nothing is acknowledged.
        await subscriber.ModifyAckDeadlineAsync(
            new ModifyAckDeadlineRequest { Subscription = subscription, AckIds = { response.ReceivedMessages.Select(m => m.AckId) }, AckDeadlineSeconds = 0 }, ct).ConfigureAwait(false);
        return [.. response.ReceivedMessages.Select(m => new UndeliveredEvent(
            m.Message.MessageId, m.Message.PublishTime?.ToDateTimeOffset(), m.Message.Attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal)))];
    }

    private static TriggerRef Map(Trigger t) =>
        new(t.Name, string.IsNullOrEmpty(t.Transport?.Pubsub?.Subscription) ? null : t.Transport.Pubsub.Subscription);

    private static string ProjectOf(Namespace ns) =>
        string.IsNullOrWhiteSpace(ns.GcpProjectId) ? throw new InvalidOperationException("This connection has no Google project id.") : ns.GcpProjectId.Trim();
}
