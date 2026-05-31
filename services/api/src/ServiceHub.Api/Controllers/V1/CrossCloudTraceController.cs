using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Cross-Cloud Message Trace — searches every connected namespace (Azure, AWS, GCP)
/// for a message carrying the specified correlation/trace ID, then returns a
/// chronological route showing how the message traveled across clouds.
///
/// Phase 1: Azure namespaces are searched in real time using PeekMessagesAsync.
/// Phase 2 (planned): AWS SQS and GCP Pub/Sub namespace searches via provider SDKs.
/// Non-Azure namespaces are included in NamespaceSummaries with WasSearched=false
/// and a human-readable SkipReason so the UI can render the cloud topology accurately.
/// </summary>
[Route(ApiRoutes.CrossCloudTrace.Base)]
[Tags("Cross-Cloud Trace")]
public sealed class CrossCloudTraceController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ILogger<CrossCloudTraceController> _logger;

    private const int MaxConcurrentNamespaces = 5;
    private const int SearchTimeoutSeconds = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossCloudTraceController"/> class.
    /// </summary>
    public CrossCloudTraceController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<CrossCloudTraceController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Traces a message by correlation/trace ID across all connected cloud namespaces.
    /// Returns every occurrence of the message in every cloud, sorted chronologically,
    /// enabling engineers to reconstruct the full multi-cloud routing path.
    /// </summary>
    /// <param name="traceId">
    /// The correlation ID or trace ID to search for. This is the value set in the
    /// message's CorrelationId property (Azure) or equivalent attribute (AWS/GCP)
    /// by the publishing service so the same ID appears at every hop.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="CrossCloudTraceResponse"/> with all hops and namespace summaries.</returns>
    /// <response code="200">Trace completed. May be partial if the search timed out.</response>
    /// <response code="400">The traceId parameter is missing or empty.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("trace")]
    [ProducesResponseType(typeof(CrossCloudTraceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CrossCloudTraceResponse>> TraceMessage(
        [FromQuery] string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Request",
                Detail = "traceId query parameter is required and cannot be empty."
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var isPartialResult = false;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(SearchTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var searchToken = linkedCts.Token;

        // ── Load all namespaces for this owner ────────────────────────────
        var allNsResult = await _namespaceRepository.GetByOwnerAsync(OwnerId, cancellationToken);
        if (allNsResult.IsFailure)
        {
            return ToActionResult<CrossCloudTraceResponse>(allNsResult.Error);
        }

        var allNamespaces = allNsResult.Value;

        _logger.LogInformation(
            "Starting cross-cloud trace for {TraceId} across {Count} namespace(s)",
            LogRedactor.SanitiseForLog(traceId), allNamespaces.Count);

        // ── Separate namespaces by cloud provider ─────────────────────────
        var azureNamespaces = allNamespaces.Where(ns => ns.Provider == CloudProviderType.Azure).ToList();
        var nonAzureNamespaces = allNamespaces.Where(ns => ns.Provider != CloudProviderType.Azure).ToList();

        // ── Search Azure namespaces in parallel ───────────────────────────
        var hops = new ConcurrentBag<CrossCloudTraceHop>();
        var azureSummaries = new ConcurrentBag<CrossCloudNamespaceSummary>();
        var entitiesSearched = 0;

        var semaphore = new SemaphoreSlim(MaxConcurrentNamespaces, MaxConcurrentNamespaces);

        var azureTasks = azureNamespaces.Select(async ns =>
        {
            await semaphore.WaitAsync(searchToken).ConfigureAwait(false);
            var nsHopCount = 0;
            try
            {
                if (ns.ConnectionString is null)
                {
                    azureSummaries.Add(new CrossCloudNamespaceSummary(
                        ns.Id, ns.DisplayName ?? ns.Name, "azure",
                        WasSearched: false, SkipReason: "No connection string configured", HopsFound: 0));
                    return;
                }

                var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
                if (unprotectResult.IsFailure)
                {
                    _logger.LogWarning("Failed to decrypt connection string for namespace {NamespaceId}", ns.Id);
                    azureSummaries.Add(new CrossCloudNamespaceSummary(
                        ns.Id, ns.DisplayName ?? ns.Name, "azure",
                        WasSearched: false, SkipReason: "Connection string decryption failed", HopsFound: 0));
                    return;
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
                        catch (OperationCanceledException) { isPartialResult = true; }
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
                                catch (OperationCanceledException) { isPartialResult = true; }
                                catch (Exception ex) when (!searchToken.IsCancellationRequested)
                                {
                                    _logger.LogWarning(ex, "Error searching subscription {Sub} in topic {Topic}", sub.Name, topic.Name);
                                }
                            });

                            await Task.WhenAll(subTasks).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { isPartialResult = true; }
                        catch (Exception ex) when (!searchToken.IsCancellationRequested)
                        {
                            _logger.LogWarning(ex, "Error listing subscriptions for topic {Topic}", topic.Name);
                        }
                    });

                    await Task.WhenAll(topicTasks).ConfigureAwait(false);
                }

                azureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, nsDisplayName, "azure",
                    WasSearched: true, SkipReason: null, HopsFound: nsHopCount));
            }
            catch (OperationCanceledException)
            {
                isPartialResult = true;
                azureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, "azure",
                    WasSearched: false, SkipReason: "Search timed out", HopsFound: nsHopCount));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching namespace {NamespaceId}", ns.Id);
                azureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, "azure",
                    WasSearched: false, SkipReason: $"Search error: {ex.Message[..Math.Min(ex.Message.Length, 80)]}", HopsFound: nsHopCount));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(azureTasks).ConfigureAwait(false);

        // ── Non-Azure namespaces: Phase 2 stubs ───────────────────────────
        var nonAzureSummaries = nonAzureNamespaces.Select(ns =>
        {
            var providerLabel = ns.Provider switch
            {
                CloudProviderType.Aws => "aws",
                CloudProviderType.Gcp => "gcp",
                _ => ns.Provider.ToString().ToLowerInvariant()
            };

            var skipReason = ns.Provider switch
            {
                CloudProviderType.Aws => "AWS SQS/SNS trace support coming in Phase 2 — correlation ID matched in message attributes",
                CloudProviderType.Gcp => "GCP Pub/Sub trace support coming in Phase 2 — trace_id matched in message attributes",
                _ => "Provider not yet supported"
            };

            return new CrossCloudNamespaceSummary(
                ns.Id, ns.DisplayName ?? ns.Name, providerLabel,
                WasSearched: false, SkipReason: skipReason, HopsFound: 0);
        }).ToList();

        // ── Build final response ──────────────────────────────────────────
        var allSummaries = azureSummaries
            .Concat(nonAzureSummaries)
            .OrderBy(s => s.CloudProvider)
            .ThenBy(s => s.NamespaceDisplayName)
            .ToList();

        // Sort hops chronologically and assign HopIndex
        var sortedHops = hops
            .OrderBy(h => h.Timestamp)
            .Select((h, i) => h with { HopIndex = i })
            .ToList();

        var cloudProviders = sortedHops
            .Select(h => h.CloudProvider)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // Also include non-Azure clouds in the provider list if those namespaces exist
        // (so the UI can show the topology even when Phase 2 hasn't run yet)
        var allCloudProviders = cloudProviders
            .Union(nonAzureNamespaces.Select(ns => ns.Provider switch
            {
                CloudProviderType.Aws => "aws",
                CloudProviderType.Gcp => "gcp",
                _ => ns.Provider.ToString().ToLowerInvariant()
            }))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        stopwatch.Stop();

        _logger.LogInformation(
            "Cross-cloud trace for {TraceId} completed: {HopCount} hops across {CloudCount} cloud(s) in {Duration}ms",
            LogRedactor.SanitiseForLog(traceId), sortedHops.Count, cloudProviders.Count, stopwatch.ElapsedMilliseconds);

        return Ok(new CrossCloudTraceResponse(
            TraceId: traceId,
            Hops: sortedHops,
            NamespaceSummaries: allSummaries,
            TotalHops: sortedHops.Count,
            CloudsInvolved: cloudProviders.Count,
            CloudProviders: allCloudProviders,
            IsMultiCloud: allCloudProviders.Count >= 2,
            NamespacesSearched: azureSummaries.Count(s => s.WasSearched),
            EntitiesSearched: entitiesSearched,
            IsPartialResult: isPartialResult,
            SearchDurationMs: stopwatch.ElapsedMilliseconds
        ));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static CrossCloudTraceHop BuildAzureHop(
        Guid namespaceId,
        string nsDisplayName,
        string entityName,
        string entityPath,
        Core.Entities.Message msg,
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
