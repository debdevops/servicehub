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
using ServiceHub.Core.Security;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Dlq;

/// <summary>What one scan of one namespace found, and — as important — what it could not confirm.</summary>
/// <param name="Outcome">Scanned, skipped on purpose, or failed.</param>
/// <param name="EntitiesExamined">Queues and subscriptions whose dead-letter queue was looked at.</param>
/// <param name="NewMessages">Dead letters seen for the first time.</param>
/// <param name="Resolved">Rows marked as no longer in the queue.</param>
/// <param name="Unconfirmed">Entities the scan could not fully confirm; their rows were left exactly as they were.</param>
/// <param name="Reason">One plain sentence for a skipped or failed scan.</param>
public sealed record NamespaceScanResult(
    ScanOutcome Outcome,
    int EntitiesExamined = 0,
    int NewMessages = 0,
    int Resolved = 0,
    int Unconfirmed = 0,
    string? Reason = null);

/// <summary>How a namespace scan ended.</summary>
public enum ScanOutcome
{
    /// <summary>The scan ran.</summary>
    Scanned,

    /// <summary>Not scanned, deliberately: looking would change the cloud's own delivery state.</summary>
    Skipped,

    /// <summary>Could not be scanned this time. Nothing was changed.</summary>
    Failed,
}

/// <summary>
/// Reads a namespace's dead-letter queues and keeps <c>DlqMessages</c> true to them (unit 2.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule that matters: absence is only ever concluded from a scan that could see.</b> A row is
/// marked Resolved only when its entity was listed, peeked to the end and found without it. A failed
/// listing, a failed or truncated peek, or an unknown entity leaves every row untouched — a provider
/// outage must never look like a queue draining. (4.0.0 got this right for a failed <i>listing</i> and
/// wrong for a failed <i>peek</i>: an entity whose peek failed reported "zero live" and its rows were
/// resolved. Here that entity is unconfirmed instead.)
/// </para>
/// <para>
/// AWS and Google Cloud have no repeatable peek — looking at a message counts as a delivery attempt
/// and can dead-letter it by accident — so they are not scanned unless the operator opts in with
/// <c>DlqMonitor:AllowDestructivePeek:{Provider}</c>.
/// </para>
/// </remarks>
public sealed class DlqScanner
{
    private const int MaxBodyPreviewLength = 500;
    private const int PeekBatchSize = 100;

    // Up to 50 batches of PeekBatchSize (5,000 messages per entity per scan). A scan that ends full at
    // the cap has not seen the whole queue and says so.
    private const int MaxScanBatchesPerEntity = 50;
    private const int LookupChunk = 400;
    private const string SubscriptionPathSegment = "/subscriptions/";
    private const string RecoveryMarkerProperty = "x-servicehub-recovery-id";

    private readonly ServiceHubDbContext _db;
    private readonly ICloudProviderRouter _router;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<DlqScanner> _logger;
    private readonly IRecoveryLedger _ledger;

    /// <summary>Creates the scanner. One per scope: it shares its scope's database context.</summary>
    public DlqScanner(
        ServiceHubDbContext db,
        ICloudProviderRouter router,
        IConfiguration configuration,
        ILogger<DlqScanner> logger,
        IRecoveryLedger ledger,
        TimeProvider? time = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Scans one namespace.</summary>
    public async Task<NamespaceScanResult> ScanAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);

        if (!_router.IsRegistered(ns.Provider))
        {
            return new NamespaceScanResult(ScanOutcome.Failed, Reason: "This build of ServiceHub has no adapter for that cloud.");
        }

        var provider = _router.Resolve(ns.Provider);

        // No non-destructive peek → looking changes what is being looked at. Decided before any call.
        if (!provider.Capabilities.SupportsRepeatablePeek
            && !_configuration.GetValue($"DlqMonitor:AllowDestructivePeek:{ns.Provider}", false))
        {
            return new NamespaceScanResult(
                ScanOutcome.Skipped,
                Reason: "Looking at a message there counts as a delivery attempt, so it is not watched automatically.");
        }

        var listed = await provider.ListEntitiesForReconciliationAsync(ns.Id, ct).ConfigureAwait(false);
        if (listed.IsFailure)
        {
            // Nothing is reconciled on a failed listing.
            _logger.LogWarning("Could not list entities for namespace {NamespaceId}: {Error}", ns.Id, listed.Error.Message);
            return new NamespaceScanResult(ScanOutcome.Failed, Reason: "The cloud could not be read.");
        }

        var scan = listed.Value;
        var receiver = provider.GetMessageReceiver();

        var examined = 0;
        var totalNew = 0;
        var resolvedInEntities = 0;
        var confirmedLive = new Dictionary<string, int>();
        var unconfirmed = new HashSet<string>();

        foreach (var entity in scan.Entities)
        {
            if (entity.EntityType is not ("Queue" or "Subscription"))
            {
                continue;
            }

            // An entity that only exists to receive another's dead letters has none of its own.
            if (!provider.CanHaveDeadLetters(entity))
            {
                continue;
            }

            // Normalised up front so the reconcile key always matches the EntityName rows are stored under.
            var (entityName, topicName, entityType) = ParseEntity(entity.Name, entity.EntityType);
            var fullName = topicName is null ? entityName : $"{topicName}{SubscriptionPathSegment}{entityName}";

            // A provider that reports live counts lets an empty queue be skipped without a peek. One that
            // cannot count (Google) is peeked unconditionally rather than trusting an always-zero count.
            if (provider.Capabilities.SupportsMessageCounts && entity.DeadLetterCount == 0)
            {
                confirmedLive[fullName] = 0;
                continue;
            }

            var result = await ScanEntityAsync(receiver, ns, entityName, topicName, entityType, fullName, provider.Capabilities.SupportsRepeatablePeek, ct).ConfigureAwait(false);
            examined++;
            totalNew += result.NewCount;
            resolvedInEntities += result.Resolved;
            if (result.Complete)
            {
                confirmedLive[fullName] = result.LiveCount;
            }
            else
            {
                unconfirmed.Add(fullName);
            }
        }

        var (reconciled, unconfirmedRows) = await ReconcileAsync(ns, scan, confirmedLive, unconfirmed, ct).ConfigureAwait(false);

        // Unconfirmed = entities whose peek could not be trusted, plus stored entities the listing did not
        // vouch for. Either way their rows were left exactly as they were.
        return new NamespaceScanResult(
            ScanOutcome.Scanned, examined, totalNew, resolvedInEntities + reconciled, Math.Max(unconfirmed.Count, unconfirmedRows));
    }

    private sealed record EntityScan(int NewCount, int LiveCount, bool Complete, int Resolved = 0);

    private async Task<EntityScan> ScanEntityAsync(
        IMessageReceiver receiver, Namespace ns, string entityName, string? topicName,
        ServiceBusEntityType entityType, string fullName, bool pagesBySequence, CancellationToken ct)
    {
        var newCount = 0;

        // A provider with a repeatable peek exposes broker-assigned sequence numbers that page and identify
        // a message. One without (AWS, Google) has only a destructive receive, whose "sequence number" is a
        // hash of the message id — stable but unordered — so it takes a single sample and matches by id.
        var useSequenceKey = pagesBySequence;

        try
        {
            var peeked = new List<Message>();
            long? from = null;
            var complete = true;

            for (var batch = 0; batch < MaxScanBatchesPerEntity; batch++)
            {
                var request = new GetMessagesRequest(
                    NamespaceId: ns.Id,
                    EntityName: topicName ?? entityName,
                    SubscriptionName: entityType == ServiceBusEntityType.Subscription ? entityName : null,
                    FromDeadLetter: true,
                    MaxMessages: PeekBatchSize,
                    FromSequenceNumber: from);

                var page = await receiver.PeekDeadLetterMessagesAsync(request, ct).ConfigureAwait(false);
                if (page.IsFailure)
                {
                    _logger.LogWarning(
                        "Could not peek the dead-letter queue of {EntityName} (batch {Batch}): {Error}",
                        LogRedactor.SanitiseForLog(fullName), batch, page.Error.Message);

                    if (batch == 0)
                    {
                        return new EntityScan(0, 0, Complete: false);
                    }

                    complete = false; // saw some, not all — keep what was seen, conclude nothing about the rest
                    break;
                }

                if (page.Value.Count == 0)
                {
                    break;
                }

                peeked.AddRange(page.Value);

                if (!useSequenceKey || page.Value.Count < PeekBatchSize)
                {
                    break;
                }

                if (batch == MaxScanBatchesPerEntity - 1)
                {
                    // Still full at the cap: more may lie beyond it, so this is not the whole queue.
                    _logger.LogWarning(
                        "The dead-letter scan of {EntityName} hit its {Cap}-message cap; more may exist beyond it.",
                        LogRedactor.SanitiseForLog(fullName), MaxScanBatchesPerEntity * PeekBatchSize);
                    complete = false;
                    break;
                }

                from = page.Value[^1].SequenceNumber + 1;
            }

            var detectedAt = _time.GetUtcNow();
            var existing = await LoadExistingAsync(ns, fullName, peeked, useSequenceKey, ct).ConfigureAwait(false);
            var newlySeen = new List<(Message Message, string BodyHash)>();

            foreach (var msg in peeked)
            {
                ct.ThrowIfCancellationRequested();

                var key = useSequenceKey ? msg.SequenceNumber.ToString() : msg.MessageId;
                if (existing.TryGetValue(key, out var row))
                {
                    // Already known. A row that came back is Active again — but a row mid-replay or
                    // mid-purge is still physically present only because the provider call has not
                    // finished, and flipping it here would race the executor's own claim.
                    if (row.Status is not (DlqMessageStatus.Active or DlqMessageStatus.Replaying or DlqMessageStatus.Purging))
                    {
                        row.Status = DlqMessageStatus.Active;
                    }

                    continue;
                }

                var bodyHash = ComputeBodyHash(msg.Body);
                newlySeen.Add((msg, bodyHash));
                _db.DlqMessages.Add(new DlqMessage
                {
                    MessageId = msg.MessageId,
                    SequenceNumber = msg.SequenceNumber,
                    BodyHash = bodyHash,
                    NamespaceId = ns.Id,
                    CloudProvider = ns.Provider,
                    OwnerId = ns.OwnerId,
                    EntityName = fullName,
                    EntityType = entityType,
                    TopicName = topicName,
                    EnqueuedTimeUtc = msg.EnqueuedTime,
                    DetectedAtUtc = detectedAt,
                    DeadLetterReason = msg.DeadLetterReason,
                    DeadLetterErrorDescription = msg.DeadLetterErrorDescription,
                    DeliveryCount = msg.DeliveryCount,
                    ContentType = msg.ContentType,
                    MessageSize = msg.SizeInBytes,
                    BodyPreview = TruncateBody(msg.Body),
                    ApplicationPropertiesJson = SerializeProperties(msg.ApplicationProperties),
                    CorrelationId = msg.CorrelationId,
                    SessionId = msg.SessionId,
                });
                newCount++;
            }

            // Rows in this entity that were not seen this time are gone — but only if this scan saw the
            // whole queue. AWS/GCP take a single sample, so a full batch proves nothing beyond it.
            var resolvedHere = 0;
            var sawEverything = complete && (useSequenceKey || peeked.Count < PeekBatchSize);
            if (sawEverything)
            {
                var seen = peeked.Select(m => useSequenceKey ? m.SequenceNumber.ToString() : m.MessageId).ToHashSet();
                var gone = await _db.DlqMessages
                    .Where(m => m.NamespaceId == ns.Id && m.EntityName == fullName && m.Status == DlqMessageStatus.Active)
                    .ToListAsync(ct).ConfigureAwait(false);

                var now = _time.GetUtcNow();
                foreach (var m in gone.Where(m => !seen.Contains(useSequenceKey ? m.SequenceNumber.ToString() : m.MessageId)))
                {
                    m.Status = DlqMessageStatus.Resolved;
                    m.ResolvedAt = now;
                    m.ResolutionCause = DlqResolutionCause.VanishedExternally;
                    resolvedHere++;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Only once the new rows are safely stored: a message that has come back is recorded against the
            // replay it came back from, so the answer to "did it work?" is never later than the sighting.
            foreach (var (message, hash) in newlySeen)
            {
                await DetectRecurrenceAsync(ns, fullName, message, hash, detectedAt, ct).ConfigureAwait(false);
            }

            return new EntityScan(newCount, peeked.Count, complete, resolvedHere);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // One entity failing must not stop the rest of the namespace — but it must not read as "empty" either.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning the dead-letter queue of {EntityName} in namespace {NamespaceId}",
                LogRedactor.SanitiseForLog(fullName), ns.Id);
            _db.ChangeTracker.Clear();
            return new EntityScan(0, 0, Complete: false);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Marks Active rows Resolved for entities that are provably empty or provably gone. Returns
    /// (resolved, skippedBecauseUnconfirmed).
    /// </summary>
    private async Task<(int Resolved, int Skipped)> ReconcileAsync(
        Namespace ns, Core.Models.EntityScanResult scan,
        Dictionary<string, int> confirmedLive, HashSet<string> unconfirmed, CancellationToken ct)
    {
        try
        {
            var activeEntities = await _db.DlqMessages
                .Where(m => m.NamespaceId == ns.Id && m.Status == DlqMessageStatus.Active)
                .Select(m => m.EntityName)
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false);

            // Absence means "gone" only if the listing was complete and this entity's peek was confirmed.
            // An incomplete listing or an unconfirmed peek means the scan did not look, not that it is gone.
            bool IsUnconfirmed(string name)
            {
                if (unconfirmed.Contains(name))
                {
                    return true;
                }

                var sub = name.IndexOf(SubscriptionPathSegment, StringComparison.Ordinal);
                if (sub < 0)
                {
                    return scan.IncompleteQueueNames.Contains(name);
                }

                return scan.SnsListingFailed
                    || scan.IncompleteTopicNames.Contains(name[..sub])
                    || scan.IncompleteQueueNames.Contains(name[(sub + SubscriptionPathSegment.Length)..]);
            }

            var skipped = activeEntities.Count(IsUnconfirmed);
            var toResolve = activeEntities
                .Where(n => !IsUnconfirmed(n))
                .Where(n => !confirmedLive.TryGetValue(n, out var live) || live == 0)
                .ToList();

            var resolved = 0;
            var now = _time.GetUtcNow();
            foreach (var name in toResolve)
            {
                var stale = await _db.DlqMessages
                    .Where(m => m.NamespaceId == ns.Id && m.EntityName == name && m.Status == DlqMessageStatus.Active)
                    .ToListAsync(ct).ConfigureAwait(false);

                foreach (var row in stale)
                {
                    // An absence proves the message is gone, not who removed it (R5).
                    row.Status = DlqMessageStatus.Resolved;
                    row.ResolvedAt = now;
                    row.ResolutionCause = DlqResolutionCause.VanishedExternally;
                    resolved++;
                }
            }

            if (resolved > 0)
            {
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return (resolved, skipped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reconciling dead letters for namespace {NamespaceId} — scan results are still usable", ns.Id);
            _db.ChangeTracker.Clear();
            return (0, 0);
        }
#pragma warning restore CA1031
    }

    private async Task<Dictionary<string, DlqMessage>> LoadExistingAsync(
        Namespace ns, string fullName, List<Message> peeked, bool useSequenceKey, CancellationToken ct)
    {
        var found = new Dictionary<string, DlqMessage>();
        foreach (var chunk in peeked.Chunk(LookupChunk))
        {
            List<DlqMessage> rows;
            if (useSequenceKey)
            {
                var sequences = chunk.Select(m => m.SequenceNumber).ToList();
                rows = await _db.DlqMessages
                    .Where(m => m.NamespaceId == ns.Id && m.EntityName == fullName && sequences.Contains(m.SequenceNumber))
                    .ToListAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var ids = chunk.Select(m => m.MessageId).ToList();
                rows = await _db.DlqMessages
                    .Where(m => m.NamespaceId == ns.Id && m.EntityName == fullName && ids.Contains(m.MessageId))
                    .ToListAsync(ct).ConfigureAwait(false);
            }

            foreach (var row in rows)
            {
                found[useSequenceKey ? row.SequenceNumber.ToString() : row.MessageId] = row;
            }
        }

        return found;
    }

    private static (string EntityName, string? TopicName, ServiceBusEntityType EntityType) ParseEntity(string fullName, string cloudEntityType)
    {
        if (cloudEntityType != "Subscription")
        {
            return (fullName, null, ServiceBusEntityType.Queue);
        }

        // Azure lists subscriptions as "topic/subscriptions/sub"; AWS and Google as "topic/sub".
        var idx = fullName.IndexOf(SubscriptionPathSegment, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return (fullName[(idx + SubscriptionPathSegment.Length)..], fullName[..idx], ServiceBusEntityType.Subscription);
        }

        var slash = fullName.LastIndexOf('/');
        return slash >= 0
            ? (fullName[(slash + 1)..], fullName[..slash], ServiceBusEntityType.Subscription)
            : (fullName, null, ServiceBusEntityType.Subscription);
    }

    /// <summary>
    /// Attributes a newly seen dead letter to a replay it may be the return of. An exact match by the stamped
    /// recovery marker wins. When no marker survived, a body-hash match in the same queue is a
    /// <i>heuristic</i>: every candidate is recorded with the number of collisions rather than guessing which.
    /// A failure here never fails the scan — the ledger's own state is authoritative.
    /// </summary>
    private async Task DetectRecurrenceAsync(
        Namespace ns, string fullName, Message message, string bodyHash, DateTimeOffset detectedAt, CancellationToken ct)
    {
        try
        {
            string? marker = null;
            if (message.ApplicationProperties is { } props && props.TryGetValue(RecoveryMarkerProperty, out var raw))
            {
                marker = raw as string ?? raw?.ToString();
            }

            if (!string.IsNullOrEmpty(marker)
                && await _ledger.FindByMarkerAsync(ns.OwnerId, marker, ct).ConfigureAwait(false) is { } exact)
            {
                await RecordReturnAsync(exact, VerificationConfidence.Exact, 0, ct).ConfigureAwait(false);
                return;
            }

            var candidates = await _ledger.FindHeuristicRecurrenceCandidatesAsync(ns.OwnerId, ns.Id, fullName, bodyHash, detectedAt, ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                await RecordReturnAsync(candidate, VerificationConfidence.Heuristic, candidates.Count, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Recording a return must never fail the scan that found it.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check whether a new dead letter in {EntityName} is a returned replay", LogRedactor.SanitiseForLog(fullName));
        }
#pragma warning restore CA1031
    }

    private async Task RecordReturnAsync(RecoveryLedgerEntry entry, VerificationConfidence confidence, int collisionCount, CancellationToken ct)
    {
        var result = await _ledger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = entry.OwnerId, Actor = Identity.ActorIdentityResolver.ResolveSystemActor("DlqMonitorAgent"),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved, Confidence = confidence,
            DetailJson = collisionCount > 0 ? JsonSerializer.Serialize(new { collisionCount }) : null,
        }, ct).ConfigureAwait(false);

        // A lost race with the verifier closing the same entry is not an error.
        if (result.IsSuccess)
        {
            _logger.LogInformation("Ledger entry {EntryId} returned to the dead-letter queue ({Confidence} match)", entry.Id, confidence);
        }
    }

    private static string ComputeBodyHash(string? body) =>
        string.IsNullOrEmpty(body) ? "empty" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static string? TruncateBody(string? body) =>
        string.IsNullOrEmpty(body) ? null : body.Length <= MaxBodyPreviewLength ? body : body[..MaxBodyPreviewLength];

    private static string? SerializeProperties(IReadOnlyDictionary<string, object>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(properties);
        }
#pragma warning disable CA1031
        catch
        {
            return null;
        }
#pragma warning restore CA1031
    }
}
