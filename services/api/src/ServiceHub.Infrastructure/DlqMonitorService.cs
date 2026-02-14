using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Monitors dead-letter queues across namespaces, detects new messages,
/// categorises failures heuristically, and persists them to SQLite.
/// </summary>
public sealed class DlqMonitorService : IDlqMonitorService
{
    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<DlqMonitorService> _logger;

    private const int MaxBodyPreviewLength = 500;
    private const int PeekBatchSize = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorService"/> class.
    /// </summary>
    public DlqMonitorService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector protector,
        ILogger<DlqMonitorService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<int>> ScanNamespaceAsync(Guid namespaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting DLQ scan for namespace {NamespaceId}", namespaceId);

        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId);
        if (nsResult.IsFailure)
        {
            _logger.LogWarning("Namespace {NamespaceId} not found, skipping DLQ scan", namespaceId);
            return Result<int>.Failure(nsResult.Error);
        }

        var ns = nsResult.Value;
        IServiceBusClientWrapper client;
        try
        {
            if (string.IsNullOrEmpty(ns.ConnectionString))
            {
                _logger.LogWarning("Namespace {NamespaceId} has no connection string, skipping DLQ scan", namespaceId);
                return Result<int>.Failure(Error.Validation(
                    "Dlq.NoConnectionString",
                    "Namespace has no connection string configured"));
            }

            var unprotectResult = _protector.Unprotect(ns.ConnectionString);
            if (unprotectResult.IsFailure)
            {
                _logger.LogWarning("Failed to unprotect connection string for namespace {NamespaceId}", namespaceId);
                return Result<int>.Failure(unprotectResult.Error);
            }

            client = _clientCache.GetOrCreate(namespaceId, unprotectResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Service Bus client for namespace {NamespaceId}", namespaceId);
            return Result<int>.Failure(Error.ExternalService(
                "Dlq.ClientFailed",
                $"Failed to create Service Bus client: {ex.Message}"));
        }

        var totalNew = 0;

        // Track all entities that we successfully scanned, with their live sequence numbers.
        // Key = fullEntityName, Value = set of sequence numbers currently in the DLQ.
        var scannedEntities = new Dictionary<string, HashSet<long>>();

        // Scan queue DLQs
        try
        {
            var queuesResult = await client.GetQueuesAsync(cancellationToken);
            if (queuesResult.IsSuccess)
            {
                foreach (var queue in queuesResult.Value)
                {
                    if (queue.DeadLetterMessageCount > 0)
                    {
                        _logger.LogInformation("Queue {Queue} has {Count} DLQ messages", 
                            queue.Name, queue.DeadLetterMessageCount);
                        var (newCount, liveSequenceNumbers) = await ScanEntityDlqAsync(
                            client, namespaceId, queue.Name, null,
                            ServiceBusEntityType.Queue, cancellationToken);
                        totalNew += newCount;
                        scannedEntities[queue.Name] = liveSequenceNumbers;
                    }
                    else
                    {
                        // Entity has 0 DLQ messages — track as empty for reconciliation
                        scannedEntities[queue.Name] = new HashSet<long>();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning queue DLQs for namespace {NamespaceId}", namespaceId);
        }

        // Scan subscription DLQs
        try
        {
            var topicsResult = await client.GetTopicsAsync(cancellationToken);
            if (topicsResult.IsSuccess)
            {
                foreach (var topic in topicsResult.Value)
                {
                    var subsResult = await client.GetSubscriptionsAsync(topic.Name, cancellationToken);
                    if (subsResult.IsSuccess)
                    {
                        foreach (var sub in subsResult.Value)
                        {
                            var fullEntityName = $"{topic.Name}/subscriptions/{sub.Name}";
                            if (sub.DeadLetterMessageCount > 0)
                            {
                                _logger.LogInformation("Subscription {Topic}/{Subscription} has {Count} DLQ messages", 
                                    topic.Name, sub.Name, sub.DeadLetterMessageCount);
                                var (newCount, liveSequenceNumbers) = await ScanEntityDlqAsync(
                                    client, namespaceId, sub.Name, topic.Name,
                                    ServiceBusEntityType.Subscription, cancellationToken);
                                totalNew += newCount;
                                scannedEntities[fullEntityName] = liveSequenceNumbers;
                            }
                            else
                            {
                                // Entity has 0 DLQ messages — track as empty for reconciliation
                                scannedEntities[fullEntityName] = new HashSet<long>();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning subscription DLQs for namespace {NamespaceId}", namespaceId);
        }

        // Reconcile: for entities with 0 DLQ messages, mark any remaining Active DB records as Replayed
        var reconciledCount = 0;
        foreach (var (entityName2, seqNums) in scannedEntities)
        {
            if (seqNums.Count == 0)
            {
                var staleRecords = await _dbContext.DlqMessages
                    .Where(m => m.NamespaceId == namespaceId
                                && m.EntityName == entityName2
                                && m.Status == DlqMessageStatus.Active)
                    .ToListAsync(cancellationToken);

                foreach (var record in staleRecords)
                {
                    record.Status = DlqMessageStatus.Replayed;
                    record.ReplayedAt = DateTimeOffset.UtcNow;
                    reconciledCount++;
                }
            }
        }

        if (reconciledCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Reconciled {Count} stale DLQ messages as Replayed for namespace {NamespaceId}",
                reconciledCount, namespaceId);
        }

        _logger.LogInformation(
            "DLQ scan complete for namespace {NamespaceId}: {NewMessages} new, {Reconciled} reconciled",
            namespaceId, totalNew, reconciledCount);

        return totalNew;
    }

    private async Task<(int NewCount, HashSet<long> LiveSequenceNumbers)> ScanEntityDlqAsync(
        IServiceBusClientWrapper client,
        Guid namespaceId,
        string entityName,
        string? topicName,
        ServiceBusEntityType entityType,
        CancellationToken cancellationToken)
    {
        var newCount = 0;
        var liveSequenceNumbers = new HashSet<long>();
        var fullEntityName = topicName != null ? $"{topicName}/subscriptions/{entityName}" : entityName;

        try
        {
            var request = new GetMessagesRequest(
                NamespaceId: namespaceId,
                EntityName: topicName ?? entityName,
                SubscriptionName: entityType == ServiceBusEntityType.Subscription ? entityName : null,
                FromDeadLetter: true,
                MaxMessages: PeekBatchSize);

            var messagesResult = await client.PeekMessagesAsync(request, cancellationToken);
            if (messagesResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to peek DLQ messages from {EntityType} {EntityName}: {Error}",
                    entityType, entityName, messagesResult.Error.Message);
                return (0, liveSequenceNumbers);
            }

            var detectedAt = DateTimeOffset.UtcNow;

            // Track which messages are currently in DLQ
            var currentDlqSequenceNumbers = messagesResult.Value
                .Select(m => m.SequenceNumber)
                .ToHashSet();

            foreach (var msg in messagesResult.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                liveSequenceNumbers.Add(msg.SequenceNumber);

                var bodyHash = ComputeBodyHash(msg.Body);

                // Check if message already exists in database
                var existingMessage = await _dbContext.DlqMessages
                    .FirstOrDefaultAsync(
                        m => m.NamespaceId == namespaceId
                             && m.EntityName == fullEntityName
                             && m.SequenceNumber == msg.SequenceNumber,
                        cancellationToken);

                if (existingMessage != null)
                {
                    // Message already tracked — ensure it's marked as Active
                    if (existingMessage.Status != DlqMessageStatus.Active)
                    {
                        existingMessage.Status = DlqMessageStatus.Active;
                        existingMessage.ResolvedAt = null;
                        existingMessage.ReplayedAt = null;
                        existingMessage.ReplaySuccess = null;
                        _logger.LogInformation(
                            "Message {MessageId} returned to DLQ, status updated to Active",
                            msg.MessageId);
                    }
                    continue;
                }

                // New message — add to database
                var (category, confidence) = CategorizeFailure(msg.DeadLetterReason, msg.DeadLetterErrorDescription, msg.DeliveryCount);

                var dlqMessage = new DlqMessage
                {
                    MessageId = msg.MessageId,
                    SequenceNumber = msg.SequenceNumber,
                    BodyHash = bodyHash,
                    NamespaceId = namespaceId,
                    EntityName = fullEntityName,
                    EntityType = entityType,
                    EnqueuedTimeUtc = msg.EnqueuedTime,
                    DeadLetterTimeUtc = msg.EnqueuedTime, // Best approximation from peek
                    DetectedAtUtc = detectedAt,
                    DeadLetterReason = msg.DeadLetterReason,
                    DeadLetterErrorDescription = msg.DeadLetterErrorDescription,
                    DeliveryCount = msg.DeliveryCount,
                    ContentType = msg.ContentType,
                    MessageSize = msg.SizeInBytes,
                    BodyPreview = TruncateBody(msg.Body),
                    ApplicationPropertiesJson = SerializeProperties(msg.ApplicationProperties),
                    FailureCategory = category,
                    CategoryConfidence = confidence,
                    Status = DlqMessageStatus.Active,
                    CorrelationId = msg.CorrelationId,
                    SessionId = msg.SessionId,
                    TopicName = topicName,
                };

                _dbContext.DlqMessages.Add(dlqMessage);
                newCount++;
            }

            // CRITICAL: Mark messages that are NO LONGER in DLQ as Replayed
            var messagesNoLongerInDlq = await _dbContext.DlqMessages
                .Where(m => m.NamespaceId == namespaceId
                            && m.EntityName == fullEntityName
                            && m.Status == DlqMessageStatus.Active
                            && !currentDlqSequenceNumbers.Contains(m.SequenceNumber))
                .ToListAsync(cancellationToken);

            foreach (var removedMessage in messagesNoLongerInDlq)
            {
                removedMessage.Status = DlqMessageStatus.Replayed;
                removedMessage.ReplayedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Message {MessageId} no longer in DLQ — marked as Replayed",
                    removedMessage.MessageId);
            }

            if (messagesNoLongerInDlq.Count > 0)
            {
                _logger.LogInformation(
                    "Marked {Count} messages as Replayed for {EntityType} {EntityName}",
                    messagesNoLongerInDlq.Count, entityType, entityName);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (newCount > 0)
            {
                _logger.LogInformation(
                    "Stored {Count} new DLQ messages from {EntityType} {EntityName}",
                    newCount, entityType, entityName);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error scanning DLQ for {EntityType} {EntityName} in namespace {NamespaceId}",
                entityType, entityName, namespaceId);
        }

        return (newCount, liveSequenceNumbers);
    }

    private static string ComputeBodyHash(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "empty";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? TruncateBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        return body.Length <= MaxBodyPreviewLength
            ? body
            : body[..MaxBodyPreviewLength];
    }

    private static string? SerializeProperties(IReadOnlyDictionary<string, object>? properties)
    {
        if (properties == null || properties.Count == 0)
            return null;

        try
        {
            return JsonSerializer.Serialize(properties);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Heuristically categorises a dead-letter failure based on reason and error description.
    /// Returns the category and a confidence score between 0.0 and 1.0.
    /// </summary>
    internal static (FailureCategory Category, double Confidence) CategorizeFailure(
        string? reason, string? errorDescription, int deliveryCount)
    {
        var combined = $"{reason} {errorDescription}".ToLowerInvariant();

        // MaxDeliveryCountExceeded is the most common and definitive
        if (reason?.Contains("MaxDeliveryCount", StringComparison.OrdinalIgnoreCase) == true
            || combined.Contains("maxdeliverycount"))
        {
            return (FailureCategory.MaxDelivery, 0.95);
        }

        // TTL expiration
        if (reason?.Contains("TTLExpiredException", StringComparison.OrdinalIgnoreCase) == true
            || combined.Contains("ttl") || combined.Contains("expired"))
        {
            return (FailureCategory.Expired, 0.90);
        }

        // Transient: timeout, connection, database errors
        if (combined.Contains("timeout") || combined.Contains("database")
            || combined.Contains("sqlexception") || combined.Contains("connection refused")
            || combined.Contains("service unavailable") || combined.Contains("transient"))
        {
            return (FailureCategory.Transient, 0.80);
        }

        // Data quality: schema, validation, deserialization
        if (combined.Contains("schema") || combined.Contains("validation")
            || combined.Contains("deserializ") || combined.Contains("json")
            || combined.Contains("format") || combined.Contains("parsing"))
        {
            return (FailureCategory.DataQuality, 0.80);
        }

        // Authorization
        if (combined.Contains("unauthorized") || combined.Contains("forbidden")
            || combined.Contains("401") || combined.Contains("403")
            || combined.Contains("permission") || combined.Contains("access denied"))
        {
            return (FailureCategory.Authorization, 0.85);
        }

        // Resource not found
        if (combined.Contains("not found") || combined.Contains("404")
            || combined.Contains("resource missing"))
        {
            return (FailureCategory.ResourceNotFound, 0.75);
        }

        // Quota / size exceeded
        if (combined.Contains("quota") || combined.Contains("size exceeded")
            || combined.Contains("too large") || combined.Contains("entity full"))
        {
            return (FailureCategory.QuotaExceeded, 0.80);
        }

        // Generic processing error: any exception-like text
        if (combined.Contains("exception") || combined.Contains("error")
            || combined.Contains("failed") || combined.Contains("processing"))
        {
            return (FailureCategory.ProcessingError, 0.50);
        }

        // Unknown
        return (FailureCategory.Unknown, 0.0);
    }
}
