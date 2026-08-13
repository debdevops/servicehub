using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
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
    private readonly IForensicEngineRouter _forensicEngine;
    private readonly IConfiguration _configuration;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly DlqNotMonitoredLogGuard _notMonitoredLogGuard;
    private readonly ILogger<DlqMonitorService> _logger;

    private const int MaxBodyPreviewLength = 500;
    private const int PeekBatchSize = 100;

    // Safety cap mirroring ServiceBusClientWrapper.GetScheduledMessagesAsync's iterative-peek
    // pattern: up to 50 batches of PeekBatchSize (5,000 messages/entity/scan cycle).
    private const int MaxScanBatchesPerEntity = 50;
    private const string SubscriptionPathSegment = "/subscriptions/";

    /// <summary>
    /// The application property/attribute a replay stamps to attribute an eventual recurrence
    /// back to the exact <see cref="RecoveryLedgerEntry"/> that produced it (§8.2 of the
    /// Recovery Evidence Ledger verification model). See the provider <c>ReplayMessageAsync</c>
    /// implementations for where this is written.
    /// </summary>
    internal const string RecoveryMarkerPropertyKey = "x-servicehub-recovery-id";

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorService"/> class.
    /// </summary>
    public DlqMonitorService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        CloudProviderRouter router,
        IForensicEngineRouter forensicEngine,
        IConfiguration configuration,
        IRecoveryLedger recoveryLedger,
        DlqNotMonitoredLogGuard notMonitoredLogGuard,
        ILogger<DlqMonitorService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _forensicEngine = forensicEngine ?? throw new ArgumentNullException(nameof(forensicEngine));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _notMonitoredLogGuard = notMonitoredLogGuard ?? throw new ArgumentNullException(nameof(notMonitoredLogGuard));
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

        // Providers that declare SupportsRepeatablePeek: false have no non-destructive peek —
        // every call is a real receive that increments the message's ReceiveCount (AWS SQS).
        // Timer-driven and manual scans alike must not silently mutate customer delivery state,
        // so this is checked before ListEntitiesAsync is ever called. An operator who accepts
        // that consequence can opt back in via DlqMonitor:AllowDestructivePeek:{Provider}.
        if (!provider.Capabilities.SupportsRepeatablePeek
            && !_configuration.GetValue($"DlqMonitor:AllowDestructivePeek:{ns.Provider}", false))
        {
            if (_notMonitoredLogGuard.ShouldLog(ns.Provider))
            {
                _logger.LogWarning(
                    "DLQ background scanning is disabled for provider {Provider}: {Notes} Set " +
                    "DlqMonitor:AllowDestructivePeek:{Provider}=true to re-enable if the ReceiveCount " +
                    "increment this causes on every scan is acceptable.",
                    ns.Provider, provider.Capabilities.Notes, ns.Provider);
            }

            return Result<int>.Failure(Error.Validation(
                "Dlq.NotMonitored",
                $"DLQ monitoring is not available for namespace '{ns.Name}': {provider.Capabilities.Notes}"));
        }

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

            // Parsed and normalized up front so the reconcile key below (scannedEntities) always
            // matches the EntityName format DlqMessage rows are stored under — Azure lists
            // subscriptions as "topic/subscriptions/sub" already, but AWS/GCP list them as
            // "topic/sub", and only the normalized "topic/subscriptions/sub" form is ever
            // persisted (see fullEntityName in ScanEntityDlqAsync). Keying on the raw
            // entity.Name here made the AWS/GCP reconcile never fire (P9).
            var (entityName, topicName, entityType) = ParseEntity(entity.Name, entity.EntityType);
            var fullEntityName = topicName != null ? $"{topicName}{SubscriptionPathSegment}{entityName}" : entityName;

            // A provider that genuinely reports live message counts (Azure, AWS — both populate
            // DeadLetterCount reliably in ListEntitiesAsync) means entities with 0 can be skipped
            // without peeking. GCP's Capabilities.SupportsMessageCounts is false (Pub/Sub has no
            // count API, so CloudEntity.DeadLetterCount is never populated) — peek unconditionally
            // rather than trusting an always-zero count. This mirrors the same capability check
            // CrossCloudTraceController uses for its own dead-letter-peek gate.
            if (provider.Capabilities.SupportsMessageCounts && entity.DeadLetterCount == 0)
            {
                scannedEntities[fullEntityName] = 0;
                continue;
            }

            if (entity.DeadLetterCount > 0)
            {
                _logger.LogInformation("{EntityType} {EntityName} has {Count} DLQ messages",
                    entity.EntityType, LogRedactor.SanitiseForLog(entity.Name), entity.DeadLetterCount);
            }

            var (newCount, liveCount) = await ScanEntityDlqAsync(
                receiver, namespaceId, entityName, topicName,
                entityType, ns.OwnerId, ns.Provider, cancellationToken);
            totalNew += newCount;
            scannedEntities[fullEntityName] = liveCount;
        }

        // Reconcile: for entities with 0 DLQ messages, mark any remaining Active DB records as
        // Resolved. This scan only proves the message is no longer in the DLQ — it cannot tell
        // whether ServiceHub replayed it, an operator drained it externally, it expired, or
        // another tool consumed it. Absence must never be reported as "Replayed" (that is a
        // specific, unverified claim); VanishedExternally is the truthful cause until a Recovery
        // Evidence Ledger (a later phase) can attribute the disappearance to a ServiceHub action.
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
                        record.Status = DlqMessageStatus.Resolved;
                        record.ResolvedAt = DateTimeOffset.UtcNow;
                        record.ResolutionCause = DlqResolutionCause.VanishedExternally;
                        reconciledCount++;
                    }
                }
            }

            if (reconciledCount > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Reconciled {Count} stale DLQ messages as Resolved (vanished externally) for namespace {NamespaceId}",
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

        // Azure sequence numbers are real broker-assigned identifiers. AWS/GCP sequence numbers
        // are a stable SHA-256 hash of the message's own (also stable) broker MessageId — they
        // do not change between peeks of the same message, but they carry no ordering relative
        // to arrival, so they cannot serve as a paging cursor (see ComputeSequenceNumber in
        // AwsMessageReceiver/GcpMessageReceiver). Those providers must dedup and reconcile by
        // MessageId instead.
        var useSequenceKey = provider == CloudProviderType.Azure;

        try
        {
            var allPeekedMessages = new List<Message>();
            long? fromSequenceNumber = null;

            for (var batch = 0; batch < MaxScanBatchesPerEntity; batch++)
            {
                var request = new GetMessagesRequest(
                    NamespaceId: namespaceId,
                    EntityName: topicName ?? entityName,
                    SubscriptionName: entityType == ServiceBusEntityType.Subscription ? entityName : null,
                    FromDeadLetter: true,
                    MaxMessages: PeekBatchSize,
                    FromSequenceNumber: fromSequenceNumber);

                var messagesResult = await receiver.PeekDeadLetterMessagesAsync(request, cancellationToken);
                if (messagesResult.IsFailure)
                {
                    if (batch == 0)
                    {
                        _logger.LogWarning(
                            "Failed to peek DLQ messages from {EntityType} {EntityName}: {Error}",
                            entityType, LogRedactor.SanitiseForLog(entityName), messagesResult.Error.Message);
                        return (0, liveCount);
                    }

                    _logger.LogWarning(
                        "Failed to peek further DLQ batch from {EntityType} {EntityName} after {Count} messages: {Error}",
                        entityType, LogRedactor.SanitiseForLog(entityName), allPeekedMessages.Count,
                        messagesResult.Error.Message);
                    break;
                }

                var batchMessages = messagesResult.Value;
                if (batchMessages.Count == 0)
                    break;

                allPeekedMessages.AddRange(batchMessages);

                // AWS/GCP sequence numbers carry no ordering relative to arrival (see
                // useSequenceKey comment above), so there is no cursor to advance past a single
                // batch there — only Azure can page beyond PeekBatchSize within a scan cycle.
                if (!useSequenceKey || batchMessages.Count < PeekBatchSize)
                    break;

                fromSequenceNumber = batchMessages[^1].SequenceNumber + 1;
            }

            var detectedAt = DateTimeOffset.UtcNow;

            foreach (var msg in allPeekedMessages)
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
                    // Message already tracked — ensure it's marked as Active. The prior
                    // ResolvedAt/ReplayedAt/ReplaySuccess/ResolutionCause are left untouched:
                    // they are evidence of what was previously recorded, and erasing them here
                    // would destroy a real recovery record for the sake of a message that turned
                    // out to have come back.
                    if (existingMessage.Status != DlqMessageStatus.Active)
                    {
                        existingMessage.Status = DlqMessageStatus.Active;
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

                // Recurrence detection: a message replayed by ServiceHub gets a brand-new
                // provider identity on every provider (P4), so a genuine return lands here, as a
                // "new" message, never in the existingMessage branch above. Detection only
                // applies to newly-detected messages for the same reason.
                await DetectRecurrenceAsync(ownerId, namespaceId, fullEntityName, bodyHash, msg, detectedAt, cancellationToken);

                var features = SignalExtractor.ExtractFeatures(dlqMessage);
                _dbContext.MessageFeatureRecords.Add(new MessageFeatureRecord
                {
                    DlqMessage = dlqMessage,
                    NamespaceId = namespaceId,
                    OwnerId = ownerId,
                    CapturedAt = detectedAt,
                    DeliveryCount = features.DeliveryCount,
                    BodySizeBytes = features.BodySizeBytes,
                    TimeToDeadletterSeconds = features.TimeToDeadletterSeconds,
                    SecondsSinceEnqueued = features.SecondsSinceEnqueued,
                    HourOfDay = features.HourOfDay,
                    DayOfWeek = features.DayOfWeek,
                    PropertyCount = features.PropertyCount,
                    Provider = features.Provider,
                    EntityName = features.EntityName,
                    DeadletterReason = features.DeadletterReason,
                    ExceptionType = features.ExceptionType,
                    ContentType = features.ContentType,
                    PayloadShape = features.PayloadShape,
                    ErrorTextNormalised = features.ErrorTextNormalised,
                    SchemaFingerprint = features.SchemaFingerprint,
                    FeatureVersion = features.FeatureVersion,
                });

                newCount++;
            }

            // Mark messages that are NO LONGER in the DLQ as Resolved. This scan cannot tell why
            // they left — ServiceHub replay, external drain, TTL expiry, or another tool are all
            // indistinguishable from here — so the cause is recorded as VanishedExternally, not
            // asserted as a replay ServiceHub never observed.
            List<DlqMessage> messagesNoLongerInDlq;
            if (useSequenceKey)
            {
                var currentDlqSequenceNumbers = allPeekedMessages
                    .Select(m => m.SequenceNumber)
                    .ToHashSet();
                messagesNoLongerInDlq = await _dbContext.DlqMessages
                    .Where(m => m.NamespaceId == namespaceId
                                && m.EntityName == fullEntityName
                                && m.Status == DlqMessageStatus.Active
                                && !currentDlqSequenceNumbers.Contains(m.SequenceNumber))
                    .ToListAsync(cancellationToken);
            }
            else if (allPeekedMessages.Count < PeekBatchSize)
            {
                // AWS/GCP have no stable pagination cursor (see useSequenceKey comment above),
                // so a single scan only ever samples up to PeekBatchSize messages. A batch at
                // the cap doesn't prove the DLQ is empty beyond it, so reconciling against a
                // possibly-truncated sample would falsely mark still-present messages as
                // Resolved. Only reconcile when this scan came in under the cap — the closest
                // signal available that it saw the entity's full live DLQ.
                var currentDlqMessageIds = allPeekedMessages
                    .Select(m => m.MessageId)
                    .ToHashSet();
                messagesNoLongerInDlq = await _dbContext.DlqMessages
                    .Where(m => m.NamespaceId == namespaceId
                                && m.EntityName == fullEntityName
                                && m.Status == DlqMessageStatus.Active
                                && !currentDlqMessageIds.Contains(m.MessageId))
                    .ToListAsync(cancellationToken);
            }
            else
            {
                messagesNoLongerInDlq = [];
                _logger.LogInformation(
                    "Skipping DLQ reconciliation for {EntityType} {EntityName}: peek sample hit the {Cap}-message cap, so it may not reflect the full live DLQ",
                    entityType, LogRedactor.SanitiseForLog(entityName), PeekBatchSize);
            }

            foreach (var removedMessage in messagesNoLongerInDlq)
            {
                removedMessage.Status = DlqMessageStatus.Resolved;
                removedMessage.ResolvedAt = DateTimeOffset.UtcNow;
                removedMessage.ResolutionCause = DlqResolutionCause.VanishedExternally;
                _logger.LogInformation(
                    "Message {MessageId} no longer in DLQ — marked as Resolved (vanished externally)",
                    LogRedactor.SanitiseForLog(removedMessage.MessageId));
            }

            if (messagesNoLongerInDlq.Count > 0)
            {
                _logger.LogInformation(
                    "Marked {Count} messages as Resolved for {EntityType} {EntityName}",
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

    /// <summary>
    /// Attributes a newly-detected DLQ message to an open <see cref="RecoveryLedgerEntry"/> it
    /// may be a recurrence of — the scan-time half of the verification model's shipping
    /// algorithm (§8.5). Exact match via the stamped marker takes priority; when the marker
    /// could not be applied (<see cref="ProviderCapabilities.SupportsRecoveryMarker"/> was false
    /// for this provider, the SQS attribute cap was hit, or the flag was disabled at replay
    /// time), falls back to a body-hash heuristic that may match more than one open entry — every
    /// match is recorded, each with its own collision count, rather than guessing which one it was.
    /// </summary>
    private async Task DetectRecurrenceAsync(
        string ownerId, Guid namespaceId, string fullEntityName, string bodyHash,
        Message msg, DateTimeOffset detectedAt, CancellationToken cancellationToken)
    {
        string? marker = null;
        if (msg.ApplicationProperties is { } props
            && props.TryGetValue(RecoveryMarkerPropertyKey, out var rawMarker))
        {
            marker = rawMarker as string ?? rawMarker?.ToString();
        }

        if (!string.IsNullOrEmpty(marker))
        {
            var exactMatch = await _recoveryLedger.FindByMarkerAsync(ownerId, marker, cancellationToken);
            if (exactMatch is not null)
            {
                await RecordRecurrenceAsync(exactMatch, VerificationConfidence.Exact, collisionCount: 0, cancellationToken);
                return;
            }
        }

        var heuristicCandidates = await _recoveryLedger.FindHeuristicRecurrenceCandidatesAsync(
            ownerId, namespaceId, fullEntityName, bodyHash, detectedAt, cancellationToken);

        foreach (var candidate in heuristicCandidates)
        {
            await RecordRecurrenceAsync(candidate, VerificationConfidence.Heuristic, heuristicCandidates.Count, cancellationToken);
        }
    }

    private async Task RecordRecurrenceAsync(
        RecoveryLedgerEntry entry, VerificationConfidence confidence, int collisionCount, CancellationToken cancellationToken)
    {
        var actor = ActorIdentityResolver.ResolveSystemActor("DlqMonitorService");
        var detail = collisionCount > 0
            ? JsonSerializer.Serialize(new { collisionCount })
            : null;

        var result = await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = entry.OwnerId,
            Actor = actor,
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = confidence,
            DetailJson = detail,
        }, cancellationToken);

        if (result.IsFailure)
        {
            // Not fatal to the scan — most commonly means a concurrent scan or the verification
            // worker's window-close sweep already closed this entry first; a lost race, not an
            // error. The entry's own state is authoritative either way.
            _logger.LogDebug(
                "Skipped recording recurrence for recovery ledger entry {EntryId}: {Error}",
                entry.Id, result.Error.Message);
        }
        else
        {
            _logger.LogInformation(
                "Recovery ledger entry {EntryId} returned to the DLQ ({Confidence} match)",
                entry.Id, confidence);
        }
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
