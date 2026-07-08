using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Services;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Cross-Cloud Message Trace — searches every connected namespace (Azure, AWS, GCP)
/// for a message carrying the specified correlation/trace ID, then returns a
/// chronological route showing how the message traveled across clouds.
/// </summary>
[Route(ApiRoutes.CrossCloudTrace.Base)]
[Tags("Cross-Cloud Trace")]
public sealed class CrossCloudTraceController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IAzureTraceSearcher _azureTraceSearcher;
    private readonly DlqDbContext _dlqContext;
    private readonly IEnumerable<ICloudMessagingProvider> _cloudProviders;
    private readonly ILogger<CrossCloudTraceController> _logger;

    private const int MaxConcurrentNamespaces = 5;
    private const int SearchTimeoutSeconds = 30;
    private const int MaxHistoryEntries = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossCloudTraceController"/> class.
    /// </summary>
    public CrossCloudTraceController(
        INamespaceRepository namespaceRepository,
        IAzureTraceSearcher azureTraceSearcher,
        DlqDbContext dlqContext,
        ILogger<CrossCloudTraceController> logger,
        IEnumerable<ICloudMessagingProvider>? cloudProviders = null)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _azureTraceSearcher = azureTraceSearcher ?? throw new ArgumentNullException(nameof(azureTraceSearcher));
        _dlqContext = dlqContext ?? throw new ArgumentNullException(nameof(dlqContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cloudProviders = cloudProviders ?? [];
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
    [HttpGet]
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
        var isPartialResultFlag = 0; // 0 = false, 1 = true (Interlocked for thread safety)

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
            try
            {
                // Azure-specific search (client cache, decryption, queue/subscription
                // active + DLQ peeking) lives in IAzureTraceSearcher so this controller
                // holds only orchestration and aggregation.
                var searchResult = await _azureTraceSearcher
                    .SearchAsync(ns, traceId, searchToken).ConfigureAwait(false);

                foreach (var hop in searchResult.Hops)
                    hops.Add(hop);

                Interlocked.Add(ref entitiesSearched, searchResult.EntitiesSearched);
                if (searchResult.IsPartial)
                    Interlocked.Exchange(ref isPartialResultFlag, 1);

                azureSummaries.Add(searchResult.Summary);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(azureTasks).ConfigureAwait(false);

        // ── Non-Azure namespaces: search via ICloudMessagingProvider ─────
        var nonAzureSummaries = new ConcurrentBag<CrossCloudNamespaceSummary>();

        var nonAzureTasks = nonAzureNamespaces.Select(async ns =>
        {
            var providerLabel = ns.Provider switch
            {
                CloudProviderType.Aws => "aws",
                CloudProviderType.Gcp => "gcp",
                _ => ns.Provider.ToString().ToLowerInvariant()
            };

            var matchingProvider = _cloudProviders.FirstOrDefault(p => p.ProviderType == ns.Provider);
            if (matchingProvider is null)
            {
                nonAzureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, providerLabel,
                    WasSearched: false,
                    SkipReason: $"{providerLabel.ToUpperInvariant()} provider is not enabled on this server.",
                    HopsFound: 0));
                return;
            }

            await semaphore.WaitAsync(searchToken).ConfigureAwait(false);
            var nsHopCount = 0;
            try
            {
                var entitiesResult = await matchingProvider.ListEntitiesAsync(ns.Id, searchToken).ConfigureAwait(false);
                if (entitiesResult.IsFailure)
                {
                    nonAzureSummaries.Add(new CrossCloudNamespaceSummary(
                        ns.Id, ns.DisplayName ?? ns.Name, providerLabel,
                        WasSearched: false, SkipReason: $"Entity list failed: {entitiesResult.Error.Message}", HopsFound: 0));
                    return;
                }

                var receiver = matchingProvider.GetMessageReceiver();
                var nsDisplayName = ns.DisplayName ?? ns.Name;
                Interlocked.Add(ref entitiesSearched, entitiesResult.Value.Count);

                var entityTasks = entitiesResult.Value.Select(async entity =>
                {
                    try
                    {
                        var peekReq = new GetMessagesRequest(
                            NamespaceId: ns.Id,
                            EntityName: entity.Name,
                            SubscriptionName: null,
                            FromDeadLetter: false,
                            MaxMessages: GetMessagesRequest.MaxAllowedMessages);

                        var peekResult = await receiver.PeekMessagesAsync(peekReq, searchToken).ConfigureAwait(false);
                        if (!peekResult.IsSuccess) return;

                        foreach (var msg in peekResult.Value)
                        {
                            // Check CorrelationId property first (AWS maps it from MessageAttributes,
                            // GCP maps it from message attributes if the sender sets it).
                            // Fall back to ApplicationProperties for providers that use attribute bags.
                            var msgCorrelationId = msg.CorrelationId
                                ?? GetCorrelationFromProperties(msg.ApplicationProperties);

                            if (string.Equals(msgCorrelationId, traceId, StringComparison.OrdinalIgnoreCase))
                            {
                                Interlocked.Increment(ref nsHopCount);
                                hops.Add(new CrossCloudTraceHop(
                                    CloudProvider: providerLabel,
                                    NamespaceId: ns.Id,
                                    NamespaceDisplayName: nsDisplayName,
                                    EntityName: entity.Name,
                                    EntityPath: entity.Name,
                                    MessageId: msg.MessageId,
                                    SequenceNumber: msg.SequenceNumber,
                                    State: msg.State.ToString(),
                                    Timestamp: msg.EnqueuedTime,
                                    DeadLetterReason: msg.DeadLetterReason,
                                    BodyPreview: msg.Body != null && msg.Body.Length > 200 ? msg.Body[..200] : msg.Body,
                                    SizeInBytes: msg.SizeInBytes,
                                    Source: "Live",
                                    HopIndex: 0));
                            }
                        }
                    }
                    catch (OperationCanceledException) { Interlocked.Exchange(ref isPartialResultFlag, 1); }
                    catch (Exception ex) when (!searchToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Error searching {Provider} entity {Entity} in namespace {NamespaceId}",
                            providerLabel, entity.Name, ns.Id);
                    }
                });

                await Task.WhenAll(entityTasks).ConfigureAwait(false);

                nonAzureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, nsDisplayName, providerLabel,
                    WasSearched: true, SkipReason: null, HopsFound: nsHopCount));
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref isPartialResultFlag, 1);
                nonAzureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, providerLabel,
                    WasSearched: false, SkipReason: "Search timed out", HopsFound: nsHopCount));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching {Provider} namespace {NamespaceId}", providerLabel, ns.Id);
                nonAzureSummaries.Add(new CrossCloudNamespaceSummary(
                    ns.Id, ns.DisplayName ?? ns.Name, providerLabel,
                    WasSearched: false, SkipReason: $"Search error: {ex.Message[..Math.Min(ex.Message.Length, 80)]}", HopsFound: nsHopCount));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(nonAzureTasks).ConfigureAwait(false);

        // ── Merge historical DLQ records from SQLite (DLQ intelligence) ───
        // Messages that were already replayed, archived, or discarded no longer
        // appear in live peeks; their DlqMessage rows keep them in the trace.
        var historyHops = new List<CrossCloudTraceHop>();
        try
        {
            // TENANT ISOLATION: Filter DLQ records by owner
            var dlqMessages = await _dlqContext.DlqMessages
                .Where(m => m.CorrelationId == traceId && m.OwnerId == OwnerId)
                .OrderBy(m => m.DetectedAtUtc)
                .Take(MaxHistoryEntries)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Prefer Live over History: a hop already found by the live search
            // supersedes its historical DLQ record. Live hops are never collapsed —
            // the same MessageId legitimately appears once per cloud/entity hop.
            var liveMessageIds = hops
                .Select(h => h.MessageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var nsDisplayNames = allNamespaces.ToDictionary(ns => ns.Id, ns => ns.DisplayName ?? ns.Name);

            foreach (var dlq in dlqMessages)
            {
                if (liveMessageIds.Contains(dlq.MessageId))
                    continue;

                var state = dlq.Status switch
                {
                    DlqMessageStatus.Active => "DeadLettered",
                    DlqMessageStatus.Replayed => "Replayed",
                    DlqMessageStatus.Archived => "DeadLettered",
                    DlqMessageStatus.Discarded => "Resolved",
                    DlqMessageStatus.ReplayFailed => "DeadLettered",
                    DlqMessageStatus.Resolved => "Resolved",
                    _ => "DeadLettered"
                };

                historyHops.Add(new CrossCloudTraceHop(
                    CloudProvider: dlq.CloudProvider switch
                    {
                        CloudProviderType.Aws => "aws",
                        CloudProviderType.Gcp => "gcp",
                        _ => dlq.CloudProvider.ToString().ToLowerInvariant()
                    },
                    NamespaceId: dlq.NamespaceId,
                    NamespaceDisplayName: nsDisplayNames.TryGetValue(dlq.NamespaceId, out var name)
                        ? name
                        : dlq.NamespaceId.ToString(),
                    EntityName: dlq.EntityName,
                    EntityPath: dlq.EntityName,
                    MessageId: dlq.MessageId,
                    SequenceNumber: dlq.SequenceNumber,
                    State: state,
                    Timestamp: dlq.DetectedAtUtc,
                    DeadLetterReason: dlq.DeadLetterReason,
                    BodyPreview: dlq.BodyPreview,
                    SizeInBytes: dlq.MessageSize,
                    Source: "History",
                    HopIndex: 0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to query historical DLQ records for trace {TraceId}",
                LogRedactor.SanitiseForLog(traceId));
        }

        // ── Build final response ──────────────────────────────────────────
        var allSummaries = azureSummaries
            .Concat(nonAzureSummaries)
            .OrderBy(s => s.CloudProvider)
            .ThenBy(s => s.NamespaceDisplayName)
            .ToList();

        // Sort hops chronologically and assign HopIndex
        var sortedHops = hops
            .Concat(historyHops)
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
            IsPartialResult: isPartialResultFlag == 1,
            SearchDurationMs: stopwatch.ElapsedMilliseconds
        ));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a correlation ID from an application-properties dictionary.
    /// Checks several common key names used by AWS SQS message attributes and GCP Pub/Sub attributes.
    /// </summary>
    private static string? GetCorrelationFromProperties(IReadOnlyDictionary<string, object>? props)
    {
        if (props is null) return null;

        // Try common correlation-ID key names in priority order
        foreach (var key in new[] { "CorrelationId", "correlationId", "correlation_id", "TraceId", "traceId", "trace_id" })
        {
            if (props.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }
}
