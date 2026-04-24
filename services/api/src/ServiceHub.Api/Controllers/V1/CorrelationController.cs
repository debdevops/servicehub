using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for correlation search — finds every message sharing a CorrelationId
/// across all queues and subscriptions in connected namespaces.
/// </summary>
[Route(ApiRoutes.Correlation.Base)]
[Tags("Correlation")]
public sealed class CorrelationController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly DlqDbContext _dlqContext;
    private readonly ILogger<CorrelationController> _logger;

    private const int MaxConcurrentNamespaces = 5;
    private const int MaxResults = 500;
    private const int SearchTimeoutSeconds = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationController"/> class.
    /// </summary>
    public CorrelationController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        DlqDbContext dlqContext,
        ILogger<CorrelationController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _dlqContext = dlqContext ?? throw new ArgumentNullException(nameof(dlqContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Searches for all messages sharing a CorrelationId across all connected
    /// namespaces (or a specific namespace) and returns a chronological timeline.
    /// </summary>
    /// <param name="correlationId">The correlation ID to search for (required).</param>
    /// <param name="namespaceId">Optional namespace ID to restrict search to a single namespace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A correlation timeline response.</returns>
    /// <response code="200">Search completed successfully.</response>
    /// <response code="400">The correlationId parameter is missing or empty.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("timeline")]
    [ProducesResponseType(typeof(CorrelationTimelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CorrelationTimelineResponse>> GetTimeline(
        [FromQuery] string? correlationId,
        [FromQuery] Guid? namespaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Request",
                Detail = "correlationId query parameter is required and cannot be empty."
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var isPartialResult = false;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(SearchTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var searchToken = linkedCts.Token;

        // ----------------------------------------------------------------
        // Determine namespaces to search
        // ----------------------------------------------------------------
        IReadOnlyList<Core.Entities.Namespace> namespacesToSearch;
        if (namespaceId.HasValue)
        {
            var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId.Value, cancellationToken);
            if (nsResult.IsFailure)
            {
                return ToActionResult<CorrelationTimelineResponse>(nsResult.Error);
            }
            namespacesToSearch = [nsResult.Value];
        }
        else
        {
            var allNsResult = await _namespaceRepository.GetByOwnerAsync(OwnerId, cancellationToken);
            if (allNsResult.IsFailure)
            {
                return ToActionResult<CorrelationTimelineResponse>(allNsResult.Error);
            }
            namespacesToSearch = allNsResult.Value;
        }

        _logger.LogInformation(
            "Starting correlation search for {CorrelationId} across {Count} namespace(s)",
            LogRedactor.SanitiseForLog(correlationId), namespacesToSearch.Count);

        // ----------------------------------------------------------------
        // Search live messages in parallel (max 5 concurrent namespaces)
        // ----------------------------------------------------------------
        var liveEntries = new ConcurrentBag<CorrelationTimelineEntry>();
        var entitiesSearched = 0;

        var semaphore = new SemaphoreSlim(MaxConcurrentNamespaces, MaxConcurrentNamespaces);
        var namespaceTasks = namespacesToSearch.Select(async ns =>
        {
            await semaphore.WaitAsync(searchToken).ConfigureAwait(false);
            try
            {
                if (ns.ConnectionString is null) return;

                var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
                if (unprotectResult.IsFailure)
                {
                    _logger.LogWarning("Failed to unprotect connection string for namespace {NamespaceId}", ns.Id);
                    return;
                }

                var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
                var nsDisplayName = ns.DisplayName ?? ns.Name;

                // ---- Search all queues ----
                var queuesResult = await wrapper.GetQueuesAsync(searchToken).ConfigureAwait(false);
                if (queuesResult.IsSuccess)
                {
                    Interlocked.Add(ref entitiesSearched, queuesResult.Value.Count);
                    var queuePeekTasks = queuesResult.Value.Select(async q =>
                    {
                        try
                        {
                            // Search active messages
                            var peekReq = new GetMessagesRequest(
                                NamespaceId: ns.Id,
                                EntityName: q.Name,
                                SubscriptionName: null,
                                FromDeadLetter: false,
                                MaxMessages: GetMessagesRequest.MaxAllowedMessages);

                            var peekResult = await wrapper.PeekMessagesAsync(peekReq, searchToken).ConfigureAwait(false);
                            if (peekResult.IsSuccess)
                            {
                                foreach (var msg in peekResult.Value)
                                {
                                    if (string.Equals(msg.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        liveEntries.Add(new CorrelationTimelineEntry(
                                            Source: "Live",
                                            NamespaceId: ns.Id,
                                            NamespaceDisplayName: nsDisplayName,
                                            EntityName: q.Name,
                                            EntityPath: q.Name,
                                            MessageId: msg.MessageId,
                                            SequenceNumber: msg.SequenceNumber,
                                            State: msg.State.ToString(),
                                            Timestamp: msg.EnqueuedTime,
                                            DeadLetterReason: msg.DeadLetterReason,
                                            BodyPreview: msg.Body != null && msg.Body.Length > 200
                                                ? msg.Body[..200]
                                                : msg.Body,
                                            SizeInBytes: msg.SizeInBytes));
                                    }
                                }
                            }

                            // Search dead-letter messages
                            if (q.DeadLetterMessageCount > 0)
                            {
                                var dlqPeekReq = new GetMessagesRequest(
                                    NamespaceId: ns.Id,
                                    EntityName: q.Name,
                                    SubscriptionName: null,
                                    FromDeadLetter: true,
                                    MaxMessages: GetMessagesRequest.MaxAllowedMessages);

                                var dlqPeekResult = await wrapper.PeekMessagesAsync(dlqPeekReq, searchToken).ConfigureAwait(false);
                                if (dlqPeekResult.IsSuccess)
                                {
                                    foreach (var msg in dlqPeekResult.Value)
                                    {
                                        if (string.Equals(msg.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            liveEntries.Add(new CorrelationTimelineEntry(
                                                Source: "Live",
                                                NamespaceId: ns.Id,
                                                NamespaceDisplayName: nsDisplayName,
                                                EntityName: q.Name,
                                                EntityPath: q.Name,
                                                MessageId: msg.MessageId,
                                                SequenceNumber: msg.SequenceNumber,
                                                State: "DeadLettered",
                                                Timestamp: msg.EnqueuedTime,
                                                DeadLetterReason: msg.DeadLetterReason,
                                                BodyPreview: msg.Body != null && msg.Body.Length > 200
                                                    ? msg.Body[..200]
                                                    : msg.Body,
                                                SizeInBytes: msg.SizeInBytes));
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Error peeking queue {QueueName} in namespace {NamespaceId}", LogRedactor.SanitiseForLog(q.Name), ns.Id);
                        }
                    });
                    await Task.WhenAll(queuePeekTasks).ConfigureAwait(false);
                }

                // ---- Search all topics / subscriptions ----
                var topicsResult = await wrapper.GetTopicsAsync(searchToken).ConfigureAwait(false);
                if (topicsResult.IsSuccess)
                {
                    var topicTasks = topicsResult.Value.Select(async topic =>
                    {
                        try
                        {
                            var subsResult = await wrapper.GetSubscriptionsAsync(topic.Name, searchToken).ConfigureAwait(false);
                            if (subsResult.IsFailure) return;

                            Interlocked.Add(ref entitiesSearched, subsResult.Value.Count);

                            var subPeekTasks = subsResult.Value.Select(async sub =>
                            {
                                try
                                {
                                    // Search active messages
                                    var peekReq = new GetMessagesRequest(
                                        NamespaceId: ns.Id,
                                        EntityName: topic.Name,
                                        SubscriptionName: sub.Name,
                                        FromDeadLetter: false,
                                        MaxMessages: GetMessagesRequest.MaxAllowedMessages);

                                    var peekResult = await wrapper.PeekMessagesAsync(peekReq, searchToken).ConfigureAwait(false);
                                    var entityPath = $"{topic.Name}/subscriptions/{sub.Name}";

                                    if (peekResult.IsSuccess)
                                    {
                                        foreach (var msg in peekResult.Value)
                                        {
                                            if (string.Equals(msg.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                liveEntries.Add(new CorrelationTimelineEntry(
                                                    Source: "Live",
                                                    NamespaceId: ns.Id,
                                                    NamespaceDisplayName: nsDisplayName,
                                                    EntityName: sub.Name,
                                                    EntityPath: entityPath,
                                                    MessageId: msg.MessageId,
                                                    SequenceNumber: msg.SequenceNumber,
                                                    State: msg.State.ToString(),
                                                    Timestamp: msg.EnqueuedTime,
                                                    DeadLetterReason: msg.DeadLetterReason,
                                                    BodyPreview: msg.Body != null && msg.Body.Length > 200
                                                        ? msg.Body[..200]
                                                        : msg.Body,
                                                    SizeInBytes: msg.SizeInBytes));
                                            }
                                        }
                                    }

                                    // Search dead-letter messages
                                    if (sub.DeadLetterMessageCount > 0)
                                    {
                                        var dlqPeekReq = new GetMessagesRequest(
                                            NamespaceId: ns.Id,
                                            EntityName: topic.Name,
                                            SubscriptionName: sub.Name,
                                            FromDeadLetter: true,
                                            MaxMessages: GetMessagesRequest.MaxAllowedMessages);

                                        var dlqPeekResult = await wrapper.PeekMessagesAsync(dlqPeekReq, searchToken).ConfigureAwait(false);
                                        if (dlqPeekResult.IsSuccess)
                                        {
                                            foreach (var msg in dlqPeekResult.Value)
                                            {
                                                if (string.Equals(msg.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    liveEntries.Add(new CorrelationTimelineEntry(
                                                        Source: "Live",
                                                        NamespaceId: ns.Id,
                                                        NamespaceDisplayName: nsDisplayName,
                                                        EntityName: sub.Name,
                                                        EntityPath: entityPath,
                                                        MessageId: msg.MessageId,
                                                        SequenceNumber: msg.SequenceNumber,
                                                        State: "DeadLettered",
                                                        Timestamp: msg.EnqueuedTime,
                                                        DeadLetterReason: msg.DeadLetterReason,
                                                        BodyPreview: msg.Body != null && msg.Body.Length > 200
                                                            ? msg.Body[..200]
                                                            : msg.Body,
                                                        SizeInBytes: msg.SizeInBytes));
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    _logger.LogWarning(ex,
                                        "Error peeking subscription {SubName} in topic {TopicName} namespace {NamespaceId}",
                                        LogRedactor.SanitiseForLog(sub.Name), LogRedactor.SanitiseForLog(topic.Name), ns.Id);
                                }
                            });
                            await Task.WhenAll(subPeekTasks).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Error searching topic {TopicName} in namespace {NamespaceId}", LogRedactor.SanitiseForLog(topic.Name), ns.Id);
                        }
                    });
                    await Task.WhenAll(topicTasks).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                isPartialResult = true;
                _logger.LogWarning("Correlation search timed out processing namespace {NamespaceId}", ns.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching namespace {NamespaceId} — skipping", ns.Id);
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(namespaceTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            isPartialResult = true;
            _logger.LogWarning("Correlation search overall timeout reached for {CorrelationId}", LogRedactor.SanitiseForLog(correlationId));
        }

        // ----------------------------------------------------------------
        // Query historical DLQ records from SQLite
        // ----------------------------------------------------------------
        var historicalEntries = new List<CorrelationTimelineEntry>();
        try
        {
            // TENANT ISOLATION: Filter DLQ records by owner
            var dlqMessages = await _dlqContext.DlqMessages
                .Where(m => m.CorrelationId == correlationId && m.OwnerId == OwnerId)
                .OrderBy(m => m.DetectedAtUtc)
                .Take(200)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Lookup namespace display names for history entries
            var nsDisplayNameCache = namespacesToSearch.ToDictionary(ns => ns.Id, ns => ns.DisplayName ?? ns.Name);

            foreach (var dlq in dlqMessages)
            {
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

                var nsDisplayName = nsDisplayNameCache.TryGetValue(dlq.NamespaceId, out var name) ? name : dlq.NamespaceId.ToString();

                historicalEntries.Add(new CorrelationTimelineEntry(
                    Source: "History",
                    NamespaceId: dlq.NamespaceId,
                    NamespaceDisplayName: nsDisplayName,
                    EntityName: dlq.EntityName,
                    EntityPath: dlq.EntityName,
                    MessageId: dlq.MessageId,
                    SequenceNumber: dlq.SequenceNumber,
                    State: state,
                    Timestamp: dlq.DetectedAtUtc,
                    DeadLetterReason: dlq.DeadLetterReason,
                    BodyPreview: dlq.BodyPreview,
                    SizeInBytes: dlq.MessageSize));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query historical DLQ records for correlation {CorrelationId}", LogRedactor.SanitiseForLog(correlationId));
        }

        // ----------------------------------------------------------------
        // Merge, deduplicate by MessageId, sort by Timestamp, cap at MaxResults
        // ----------------------------------------------------------------
        var allEntries = liveEntries
            .Concat(historicalEntries)
            .GroupBy(e => e.MessageId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.Source == "Live").First()) // prefer Live over History
            .OrderBy(e => e.Timestamp)
            .Take(MaxResults)
            .ToList();

        stopwatch.Stop();

        _logger.LogInformation(
            "Correlation search for {CorrelationId} complete: {Count} entries in {Ms}ms (partial={Partial})",
            LogRedactor.SanitiseForLog(correlationId), allEntries.Count, stopwatch.ElapsedMilliseconds, isPartialResult);

        var response = new CorrelationTimelineResponse(
            CorrelationId: correlationId,
            Entries: allEntries,
            TotalCount: allEntries.Count,
            NamespacesSearched: namespacesToSearch.Count,
            EntitiesSearched: entitiesSearched,
            IsPartialResult: isPartialResult,
            SearchDurationMs: stopwatch.ElapsedMilliseconds);

        return Ok(response);
    }
}
