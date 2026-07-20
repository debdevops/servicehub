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
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Monitors dead-letter queues across namespaces, detects new messages,
/// categorises failures heuristically, and persists them to SQLite.
/// Rules are evaluated manually only via Test or Replay All actions.
/// </summary>
public sealed class DlqMonitorService : IDlqMonitorService
{
    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly CloudProviderRouter _router;
    private readonly IForensicEngine _forensicEngine;
    private readonly ILogger<DlqMonitorService> _logger;

    private const int MaxBodyPreviewLength = 500;
    private const int PeekBatchSize = 100;
    private const string SubscriptionPathSegment = "/subscriptions/";

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorService"/> class.
    /// </summary>
    public DlqMonitorService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        CloudProviderRouter router,
        IForensicEngine forensicEngine,
        ILogger<DlqMonitorService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _forensicEngine = forensicEngine ?? throw new ArgumentNullException(nameof(forensicEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<int>> ScanNamespaceAsync(Guid namespaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting DLQ scan for namespace {NamespaceId}", namespaceId);

        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (nsResult.IsFailure)
        {
            _logger.LogWarning("Namespace {NamespaceId} not found, skipping DLQ scan", namespaceId);
            return Result<int>.Failure(nsResult.Error);
        }

        var ns = nsResult.Value;

        if (!_router.IsRegistered(ns.Provider))
        {
            _logger.LogInformation(
                "Skipping DLQ scan for namespace {NamespaceId}: no ICloudMessagingProvider registered for provider {Provider}",
                namespaceId, ns.Provider);
            return Result<int>.Failure(Error.Validation(
                "Dlq.ProviderNotSupported",
                $"DLQ monitoring is not available for namespace '{ns.Name}': no provider is registered for '{ns.Provider}'."));
        }

        var provider = _router.Resolve(ns.Provider);

        var entitiesResult = await provider.ListEntitiesAsync(namespaceId, cancellationToken);
        if (entitiesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to list entities for namespace {NamespaceId}: {Error}",
                namespaceId, entitiesResult.Error.Message);
            return Result<int>.Failure(entitiesResult.Error);
        }

        var receiver = provider.GetMessageReceiver();
        var totalNew = 0;

        // Track all entities that we successfully scanned, with their live DLQ message counts.
        // Key = fullEntityName, Value = number of messages currently in the DLQ.
        var scannedEntities = new Dictionary<string, int>();

        foreach (var entity in entitiesResult.Value)
        {
            if (entity.EntityType is not ("Queue" or "Subscription"))
                continue;

            // GCP dead-letter subscriptions follow the "{subscription}-dlq" naming convention;
            // they are DLQs themselves and must not be scanned for their own dead letters.
            if (ns.Provider == CloudProviderType.Gcp && entity.Name.EndsWith("-dlq", StringComparison.Ordinal))
                continue;

            // Azure reports DeadLetterCount, so entities with 0 can be skipped without peeking.
            // AWS/GCP entity listings do not populate DeadLetterCount — peek unconditionally.
            if (ns.Provider == CloudProviderType.Azure && entity.DeadLetterCount == 0)
            {
                scannedEntities[entity.Name] = 0;
                continue;
            }

            var (entityName, topicName, entityType) = ParseEntity(entity.Name, entity.EntityType);

            if (entity.DeadLetterCount > 0)
            {
                _logger.LogInformation("{EntityType} {EntityName} has {Count} DLQ messages",
                    entity.EntityType, LogRedactor.SanitiseForLog(entity.Name), entity.DeadLetterCount);
            }

            var (newCount, liveCount) = await ScanEntityDlqAsync(
                receiver, namespaceId, entityName, topicName,
                entityType, ns.OwnerId, ns.Provider, cancellationToken);
            totalNew += newCount;
            scannedEntities[entity.Name] = liveCount;
        }

        // Reconcile: for entities with 0 DLQ messages, mark any remaining Active DB records as Replayed
        var reconciledCount = 0;
        try
        {
            foreach (var (entityName2, liveCount) in scannedEntities)
            {
                if (liveCount == 0)
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error during DLQ reconciliation for namespace {NamespaceId} — scan results still usable",
                namespaceId);
        }

        _logger.LogInformation(
            "DLQ scan complete for namespace {NamespaceId}: {NewMessages} new, {Reconciled} reconciled",
            namespaceId, totalNew, reconciledCount);

        return totalNew;
    }

    private static (string EntityName, string? TopicName, ServiceBusEntityType EntityType) ParseEntity(
        string fullName, string cloudEntityType)
    {
        if (cloudEntityType == "Subscription")
        {
            // Azure subscriptions are listed as "topic/subscriptions/subscription".
            var idx = fullName.IndexOf(SubscriptionPathSegment, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return (fullName[(idx + SubscriptionPathSegment.Length)..], fullName[..idx],
                    ServiceBusEntityType.Subscription);
            }

            // AWS SNS fanout endpoints and GCP subscriptions are listed as "topic/subscription".
            var slashIdx = fullName.LastIndexOf('/');
            if (slashIdx >= 0)
            {
                return (fullName[(slashIdx + 1)..], fullName[..slashIdx], ServiceBusEntityType.Subscription);
            }

            return (fullName, null, ServiceBusEntityType.Subscription);
        }

        return (fullName, null, ServiceBusEntityType.Queue);
    }

    private async Task<(int NewCount, int LiveCount)> ScanEntityDlqAsync(
        IMessageReceiver receiver,
        Guid namespaceId,
        string entityName,
        string? topicName,
        ServiceBusEntityType entityType,
        string ownerId,
        CloudProviderType provider,
        CancellationToken cancellationToken)
    {
        var newCount = 0;
        var liveCount = 0;
        var fullEntityName = topicName != null ? $"{topicName}{SubscriptionPathSegment}{entityName}" : entityName;

        // Azure sequence numbers are stable identifiers; AWS/GCP sequence numbers are hashes
        // of per-delivery receipt handles / ack IDs and change on every peek, so those
        // providers must dedup and reconcile by MessageId instead.
        var useSequenceKey = provider == CloudProviderType.Azure;

        try
        {
            var request = new GetMessagesRequest(
                NamespaceId: namespaceId,
                EntityName: topicName ?? entityName,
                SubscriptionName: entityType == ServiceBusEntityType.Subscription ? entityName : null,
                FromDeadLetter: true,
                MaxMessages: PeekBatchSize);

            var messagesResult = await receiver.PeekDeadLetterMessagesAsync(request, cancellationToken);
            if (messagesResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to peek DLQ messages from {EntityType} {EntityName}: {Error}",
                    entityType, LogRedactor.SanitiseForLog(entityName), messagesResult.Error.Message);
                return (0, liveCount);
            }

            var detectedAt = DateTimeOffset.UtcNow;

            foreach (var msg in messagesResult.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                liveCount++;

                var bodyHash = ComputeBodyHash(msg.Body);

                // Check if message already exists in database
                var existingMessage = useSequenceKey
                    ? await _dbContext.DlqMessages
                        .FirstOrDefaultAsync(
                            m => m.NamespaceId == namespaceId
                                 && m.EntityName == fullEntityName
                                 && m.SequenceNumber == msg.SequenceNumber,
                            cancellationToken)
                    : await _dbContext.DlqMessages
                        .FirstOrDefaultAsync(
                            m => m.NamespaceId == namespaceId
                                 && m.EntityName == fullEntityName
                                 && m.MessageId == msg.MessageId,
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
                            LogRedactor.SanitiseForLog(msg.MessageId));
                    }
                    continue;
                }

                // New message — build entity, then run forensic analysis
                var dlqMessage = new DlqMessage
                {
                    MessageId = msg.MessageId,
                    SequenceNumber = msg.SequenceNumber,
                    BodyHash = bodyHash,
                    NamespaceId = namespaceId,
                    OwnerId = ownerId,
                    CloudProvider = provider,
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
                    Status = DlqMessageStatus.Active,
                    CorrelationId = msg.CorrelationId,
                    SessionId = msg.SessionId,
                    TopicName = topicName,
                };

                var forensic = _forensicEngine.Analyse(dlqMessage);
                dlqMessage.FailureCategory = forensic.Category;
                dlqMessage.CategoryConfidence = forensic.Confidence;
                dlqMessage.ForensicRootCause = forensic.RootCause;
                dlqMessage.ForensicConfidence = forensic.Confidence;
                dlqMessage.ReplaySafety = forensic.ReplaySafety;

                _dbContext.DlqMessages.Add(dlqMessage);
                newCount++;
            }

            // CRITICAL: Mark messages that are NO LONGER in DLQ as Replayed
            List<DlqMessage> messagesNoLongerInDlq;
            if (useSequenceKey)
            {
                var currentDlqSequenceNumbers = messagesResult.Value
                    .Select(m => m.SequenceNumber)
                    .ToHashSet();
                messagesNoLongerInDlq = await _dbContext.DlqMessages
                    .Where(m => m.NamespaceId == namespaceId
                                && m.EntityName == fullEntityName
                                && m.Status == DlqMessageStatus.Active
                                && !currentDlqSequenceNumbers.Contains(m.SequenceNumber))
                    .ToListAsync(cancellationToken);
            }
            else
            {
                var currentDlqMessageIds = messagesResult.Value
                    .Select(m => m.MessageId)
                    .ToHashSet();
                messagesNoLongerInDlq = await _dbContext.DlqMessages
                    .Where(m => m.NamespaceId == namespaceId
                                && m.EntityName == fullEntityName
                                && m.Status == DlqMessageStatus.Active
                                && !currentDlqMessageIds.Contains(m.MessageId))
                    .ToListAsync(cancellationToken);
            }

            foreach (var removedMessage in messagesNoLongerInDlq)
            {
                removedMessage.Status = DlqMessageStatus.Replayed;
                removedMessage.ReplayedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Message {MessageId} no longer in DLQ — marked as Replayed",
                    LogRedactor.SanitiseForLog(removedMessage.MessageId));
            }

            if (messagesNoLongerInDlq.Count > 0)
            {
                _logger.LogInformation(
                    "Marked {Count} messages as Replayed for {EntityType} {EntityName}",
                    messagesNoLongerInDlq.Count, entityType, LogRedactor.SanitiseForLog(entityName));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (newCount > 0)
            {
                _logger.LogInformation(
                    "Stored {Count} new DLQ messages from {EntityType} {EntityName}",
                    newCount, entityType, LogRedactor.SanitiseForLog(entityName));
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
                entityType, LogRedactor.SanitiseForLog(entityName), namespaceId);
        }

        return (newCount, liveCount);
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
}
