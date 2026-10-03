using System.Collections.Concurrent;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Services;

/// <summary>
/// Azure Service Bus implementation of <see cref="IAzureTraceSearcher"/>.
/// Contains the queue/topic-subscription active + dead-letter search algorithm
/// previously inlined in <c>CrossCloudTraceController</c>.
/// </summary>
public sealed class AzureTraceSearcher : IAzureTraceSearcher
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ILogger<AzureTraceSearcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureTraceSearcher"/> class.
    /// </summary>
    public AzureTraceSearcher(
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<AzureTraceSearcher> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AzureTraceSearchResult> SearchAsync(Namespace ns, string traceId, CancellationToken searchToken)
    {
        var hops = new ConcurrentBag<CrossCloudTraceHop>();
        var entitiesSearched = 0;
        var isPartial = 0;
        var nsHopCount = 0;

        try
        {
            if (ns.ConnectionString is null)
            {
                return new AzureTraceSearchResult(
                    [],
                    new CrossCloudNamespaceSummary(
                        ns.Id, ns.DisplayName ?? ns.Name, "azure",
                        WasSearched: false, SkipReason: "No connection string configured", HopsFound: 0),
                    0, false);
            }

            var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
            if (unprotectResult.IsFailure)
            {
                _logger.LogWarning("Failed to decrypt connection string for namespace {NamespaceId}", ns.Id);
                return new AzureTraceSearchResult(
                    [],
                    new CrossCloudNamespaceSummary(
                        ns.Id, ns.DisplayName ?? ns.Name, "azure",
                        WasSearched: false, SkipReason: "Connection string decryption failed", HopsFound: 0),
                    0, false);
            }

            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
            var nsDisplayName = ns.DisplayName ?? ns.Name;

            // ── Search queues ────────────────────────────────────────
            var queuesResult = await wrapper.GetQueuesAsync(searchToken).ConfigureAwait(false);
            if (queuesResult.IsSuccess)
            {
                Interlocked.Add(ref entitiesSearched, queuesResult.Value.Count * 2); // active + DLQ

                var queueTasks = queuesResult.Value.Select(async q =>
                {
                    try
                    {
                        // Active messages
                        var peekResult = await wrapper.PeekMessagesAsync(
                            new GetMessagesRequest(ns.Id, q.Name, null, false, GetMessagesRequest.MaxAllowedMessages),
                            searchToken).ConfigureAwait(false);

                        if (peekResult.IsSuccess)
                        {
                            foreach (var msg in peekResult.Value)
                            {
                                if (string.Equals(msg.CorrelationId, traceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    Interlocked.Increment(ref nsHopCount);
                                    hops.Add(BuildAzureHop(ns.Id, nsDisplayName, q.Name, q.Name, msg, "Live"));
                                }
                            }
                        }

                        // Dead-letter queue
                        if (q.DeadLetterMessageCount > 0)
                        {
                            var dlqResult = await wrapper.PeekMessagesAsync(
                                new GetMessagesRequest(ns.Id, q.Name, null, true, GetMessagesRequest.MaxAllowedMessages),
                                searchToken).ConfigureAwait(false);

                            if (dlqResult.IsSuccess)
                            {
                                foreach (var msg in dlqResult.Value)
                                {
                                    if (string.Equals(msg.CorrelationId, traceId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Interlocked.Increment(ref nsHopCount);
                                        hops.Add(BuildAzureHop(ns.Id, nsDisplayName, $"{q.Name}/$DeadLetterQueue", q.Name, msg, "Live"));
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { Interlocked.Exchange(ref isPartial, 1); }
                    catch (Exception ex) when (!searchToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Error searching queue {Queue} in namespace {NamespaceId}", q.Name, ns.Id);
                    }
                });

                await Task.WhenAll(queueTasks).ConfigureAwait(false);
            }

            // ── Search topic subscriptions ───────────────────────────
            var topicsResult = await wrapper.GetTopicsAsync(searchToken).ConfigureAwait(false);
            if (topicsResult.IsSuccess)
            {
                var topicTasks = topicsResult.Value.Select(async topic =>
                {
                    try
                    {
                        var subsResult = await wrapper.GetSubscriptionsAsync(topic.Name, searchToken).ConfigureAwait(false);
                        if (!subsResult.IsSuccess) return;

                        Interlocked.Add(ref entitiesSearched, subsResult.Value.Count * 2);

                        var subTasks = subsResult.Value.Select(async sub =>
                        {
                            try
                            {
                                var entityPath = $"{topic.Name}/subscriptions/{sub.Name}";

                                var peekResult = await wrapper.PeekMessagesAsync(
                                    new GetMessagesRequest(ns.Id, topic.Name, sub.Name, false, GetMessagesRequest.MaxAllowedMessages),
                                    searchToken).ConfigureAwait(false);

                                if (peekResult.IsSuccess)
                                {
                                    foreach (var msg in peekResult.Value)
                                    {
                                        if (string.Equals(msg.CorrelationId, traceId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            Interlocked.Increment(ref nsHopCount);
                                            hops.Add(BuildAzureHop(ns.Id, nsDisplayName, sub.Name, entityPath, msg, "Live"));
                                        }
                                    }
                                }

                                // DLQ for subscription
                                if (sub.DeadLetterMessageCount > 0)
                                {
                                    var dlqResult = await wrapper.PeekMessagesAsync(
                                        new GetMessagesRequest(ns.Id, topic.Name, sub.Name, true, GetMessagesRequest.MaxAllowedMessages),
                                        searchToken).ConfigureAwait(false);

                                    if (dlqResult.IsSuccess)
                                    {
                                        foreach (var msg in dlqResult.Value)
                                        {
                                            if (string.Equals(msg.CorrelationId, traceId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                Interlocked.Increment(ref nsHopCount);
                                                hops.Add(BuildAzureHop(ns.Id, nsDisplayName, sub.Name, $"{entityPath}/$DeadLetterQueue", msg, "Live"));
                                            }
                                        }
                                    }
                                }
                            }
                            catch (OperationCanceledException) { Interlocked.Exchange(ref isPartial, 1); }
                            catch (Exception ex) when (!searchToken.IsCancellationRequested)
                            {
                                _logger.LogWarning(ex, "Error searching subscription {Sub} in topic {Topic}", sub.Name, topic.Name);
                            }
                        });

                        await Task.WhenAll(subTasks).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { Interlocked.Exchange(ref isPartial, 1); }
                    catch (Exception ex) when (!searchToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Error listing subscriptions for topic {Topic}", topic.Name);
                    }
                });

                await Task.WhenAll(topicTasks).ConfigureAwait(false);
            }

            return new AzureTraceSearchResult(
                hops.ToList(),
                new CrossCloudNamespaceSummary(
                    ns.Id, nsDisplayName, "azure",
                    WasSearched: true, SkipReason: null, HopsFound: nsHopCount),
                entitiesSearched, isPartial == 1);
        }
        catch (OperationCanceledException)
        {
            return new AzureTraceSearchResult(
                hops.ToList(),
                new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, "azure",
                    WasSearched: false, SkipReason: "Search timed out", HopsFound: nsHopCount),
                entitiesSearched, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching namespace {NamespaceId}", ns.Id);
            return new AzureTraceSearchResult(
                hops.ToList(),
                new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, "azure",
                    WasSearched: false, SkipReason: $"Search error: {ex.Message[..Math.Min(ex.Message.Length, 80)]}", HopsFound: nsHopCount),
                entitiesSearched, isPartial == 1);
        }
    }

    private static CrossCloudTraceHop BuildAzureHop(
        Guid namespaceId,
        string nsDisplayName,
        string entityName,
        string entityPath,
        Message msg,
        string source)
    {
        return new CrossCloudTraceHop(
            CloudProvider: "azure",
            NamespaceId: namespaceId,
            NamespaceDisplayName: nsDisplayName,
            EntityName: entityName,
            EntityPath: entityPath,
            MessageId: msg.MessageId,
            SequenceNumber: msg.SequenceNumber,
            State: msg.State.ToString(),
            Timestamp: msg.EnqueuedTime,
            DeadLetterReason: msg.DeadLetterReason,
            BodyPreview: msg.Body != null && msg.Body.Length > 200 ? msg.Body[..200] : msg.Body,
            SizeInBytes: msg.SizeInBytes,
            Source: source,
            HopIndex: 0 // reassigned after sort
        );
    }
}
