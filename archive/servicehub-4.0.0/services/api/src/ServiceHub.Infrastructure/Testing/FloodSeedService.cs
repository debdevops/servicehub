using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Testing;

/// <summary>
/// Local test infrastructure for the high-volume/flood verification pass: procedurally generates
/// realistic <see cref="Namespace"/> and <see cref="DlqMessage"/> rows directly against the same
/// <see cref="DlqDbContext"/> the running application uses, so downstream pillars (DLQ Overview,
/// DLQ Intelligence, Incident Center, Fleet Overview, bulk operations, pagination/filtering) are
/// exercised against realistic volume without needing real cloud infrastructure at that scale.
/// <para>
/// Every generated message is run through the real <see cref="IForensicEngineRouter"/> and
/// <see cref="SignalExtractor"/> — the same classification/feature-extraction path
/// <c>DlqMonitorService</c> uses for a live scan — so category/signature/incident aggregation is
/// exercised by real code, not pre-baked labels. What this does <b>not</b> exercise: the live
/// provider scan/ingestion path itself (dedup-by-peek, scan-cap pagination, provider-specific
/// listing) and non-DLQ "active message" counts, which are never persisted and are always read
/// live from <see cref="ICloudMessagingProvider"/> — flooding those would require real (or
/// live-wired mock) provider infrastructure, out of scope for this DB-side seeder.
/// </para>
/// Registered only when explicitly wired in (see <c>Program.cs</c>) — this is not part of the
/// product's normal request path.
/// </summary>
public sealed class FloodSeedService
{
    private readonly DlqDbContext _dbContext;
    private readonly IForensicEngineRouter _forensicRouter;
    private readonly ILogger<FloodSeedService> _logger;

    private const int MaxBodyPreviewLength = 500;
    private const int SaveBatchSize = 1000;

    private static readonly string[] DomainWords =
    [
        "orders", "payments", "inventory", "shipping", "notifications", "billing", "fraud-detection",
        "user-events", "audit-log", "catalog-sync", "returns", "refunds", "loyalty", "pricing",
        "search-index", "recommendation", "email-dispatch", "sms-dispatch", "webhook-relay",
        "analytics-ingest", "session-events", "cart-abandonment", "subscription-renewal",
        "tax-calculation", "warehouse-sync", "carrier-tracking", "customer-support", "chat-events",
        "review-moderation", "promo-engine",
    ];

    private static readonly (string Reason, string ErrorHint)[] AzureDeadLetterReasons =
    [
        ("MaxDeliveryCountExceeded", "Message exceeded max delivery count after {0} attempts"),
        ("DeserializationError", "Failed to deserialize message body: unexpected token at position {1}"),
        ("ValidationFailed", "Schema validation failed: required field 'accountId' is missing"),
        ("ProcessingTimeout", "Handler did not complete within the configured processing timeout"),
        ("AuthorizationFailed", "Caller is not authorized to process this entity"),
        ("SessionLockLost", "Session lock was lost before message processing completed"),
    ];

    private static readonly (string Label, string ErrorHint)[] HeuristicDeadLetterReasons =
    [
        ("ProcessingError", "NullReferenceException: Object reference not set to an instance of an object"),
        ("DataQuality", "JsonException: Invalid JSON — unexpected character encountered while parsing value"),
        ("Transient", "TimeoutException: The operation timed out while connecting to a downstream service"),
        ("ValidationError", "ValidationException: required property 'orderId' is missing from the payload"),
        ("MaxDelivery", "Message received {0} times and exceeded the configured maxReceiveCount"),
        ("Authorization", "AccessDeniedException: caller is not authorized to perform this action"),
        ("Timeout", "RequestTimeoutException: downstream call exceeded the configured deadline"),
        ("Unknown", "An unexpected error occurred while processing this message"),
    ];

    public FloodSeedService(DlqDbContext dbContext, IForensicEngineRouter forensicRouter, ILogger<FloodSeedService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _forensicRouter = forensicRouter ?? throw new ArgumentNullException(nameof(forensicRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FloodSeedResult> SeedAsync(FloodSeedOptions options, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var started = DateTimeOffset.UtcNow;
        var rng = new Random(options.Seed);
        var providers = new[] { CloudProviderType.Azure, CloudProviderType.Aws, CloudProviderType.Gcp };
        var environments = new[] { EnvironmentType.Dev, EnvironmentType.Dev, EnvironmentType.Uat, EnvironmentType.Prod };

        var namespaceIds = new List<Guid>();
        var namespacesCreated = 0;
        var entitiesCreated = 0;
        var dlqCreated = 0;
        var pendingSinceSave = 0;

        foreach (var provider in providers)
        {
            for (var n = 0; n < options.NamespacesPerProvider; n++)
            {
                var env = environments[rng.Next(environments.Length)];
                var nsName = $"flood-{provider.ToString().ToLowerInvariant()}-{env.ToString().ToLowerInvariant()}-{n:D3}-{RandomSuffix(rng)}";

                var nsResult = Namespace.Create(
                    name: nsName,
                    connectionString: BuildSyntheticConnectionString(provider, nsName),
                    displayName: $"Flood Test — {provider} #{n:D2} ({env})",
                    description: "Generated by the high-volume/flood verification pass (local test infrastructure; not a real cloud connection).",
                    environment: env,
                    provider: provider,
                    ownerId: ownerId,
                    awsRegion: provider == CloudProviderType.Aws ? "us-east-1" : null,
                    gcpProjectId: provider == CloudProviderType.Gcp ? "servicehub-flood-test" : null);

                if (nsResult.IsFailure)
                {
                    _logger.LogWarning("Flood seed: skipped namespace {Name}: {Error}", nsName, nsResult.Error.Message);
                    continue;
                }

                var ns = nsResult.Value;
                _dbContext.Namespaces.Add(ns);
                namespaceIds.Add(ns.Id);
                namespacesCreated++;

                var entityCount = rng.Next(options.MinEntitiesPerNamespace, options.MaxEntitiesPerNamespace + 1);
                for (var e = 0; e < entityCount; e++)
                {
                    var (entityName, entityType) = BuildEntityIdentity(provider, e, rng);
                    entitiesCreated++;

                    var isMega = e < options.MegaEntitiesPerNamespace;
                    var dlqCount = isMega
                        ? rng.Next(options.MegaEntityDlqMax / 2, options.MegaEntityDlqMax + 1)
                        : rng.Next(options.MinDlqPerEntity, options.MaxDlqPerEntity + 1);

                    for (var i = 0; i < dlqCount; i++)
                    {
                        var dlqMessage = BuildDlqMessage(ns, entityName, entityType, provider, i, rng);

                        var forensic = _forensicRouter.Analyse(dlqMessage);
                        dlqMessage.FailureCategory = forensic.Category;
                        dlqMessage.CategoryConfidence = forensic.Confidence;
                        dlqMessage.ForensicRootCause = forensic.RootCause;
                        dlqMessage.ForensicConfidence = forensic.Confidence;
                        dlqMessage.ReplaySafety = forensic.ReplaySafety;

                        _dbContext.DlqMessages.Add(dlqMessage);

                        var features = SignalExtractor.ExtractFeatures(dlqMessage);
                        _dbContext.MessageFeatureRecords.Add(new MessageFeatureRecord
                        {
                            DlqMessage = dlqMessage,
                            NamespaceId = ns.Id,
                            OwnerId = ownerId,
                            CapturedAt = DateTimeOffset.UtcNow,
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

                        dlqCreated++;
                        pendingSinceSave++;

                        if (pendingSinceSave >= SaveBatchSize)
                        {
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            pendingSinceSave = 0;
                        }
                    }
                }
            }
        }

        if (pendingSinceSave > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var elapsed = DateTimeOffset.UtcNow - started;
        _logger.LogInformation(
            "Flood seed complete: {Namespaces} namespaces, {Entities} entities, {Dlq} DLQ messages in {Elapsed}",
            namespacesCreated, entitiesCreated, dlqCreated, elapsed);

        return new FloodSeedResult(namespacesCreated, entitiesCreated, dlqCreated, namespaceIds, elapsed);
    }

    private DlqMessage BuildDlqMessage(Namespace ns, string entityName, ServiceBusEntityType entityType, CloudProviderType provider, int i, Random rng)
    {
        var age = (i % 5) switch
        {
            0 => TimeSpan.FromSeconds(rng.Next(5, 120)),
            1 => TimeSpan.FromMinutes(rng.Next(1, 60)),
            2 => TimeSpan.FromHours(rng.Next(1, 24)),
            3 => TimeSpan.FromDays(rng.Next(1, 14)),
            _ => TimeSpan.FromDays(rng.Next(14, 60)),
        };

        var enqueued = DateTimeOffset.UtcNow - age;
        var deliveryCount = rng.Next(1, 12);
        var body = BuildFloodBody(rng, i);
        var bodyHash = ComputeBodyHash(body + Guid.NewGuid()); // uniqueness: two flood messages must never collide on dedup key

        string reason;
        string errorDescription;
        if (provider == CloudProviderType.Azure)
        {
            var (r, hint) = AzureDeadLetterReasons[rng.Next(AzureDeadLetterReasons.Length)];
            reason = r;
            errorDescription = string.Format(hint, deliveryCount, rng.Next(1, 999));
        }
        else
        {
            var (r, hint) = HeuristicDeadLetterReasons[rng.Next(HeuristicDeadLetterReasons.Length)];
            reason = r;
            errorDescription = string.Format(hint, deliveryCount);
        }

        var props = new Dictionary<string, object> { ["source"] = "flood-seed", ["provider"] = provider.ToString() };

        return new DlqMessage
        {
            MessageId = $"flood-{ns.Id:N}-{Sanitize(entityName)}-{i:D6}-{Guid.NewGuid():N}",
            SequenceNumber = i + 1,
            BodyHash = bodyHash,
            NamespaceId = ns.Id,
            CloudProvider = provider,
            OwnerId = ns.OwnerId,
            EntityName = entityName,
            EntityType = entityType,
            EnqueuedTimeUtc = enqueued,
            DeadLetterTimeUtc = enqueued,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = reason,
            DeadLetterErrorDescription = errorDescription,
            DeliveryCount = deliveryCount,
            ContentType = "application/json",
            MessageSize = body.Length,
            BodyPreview = body.Length <= MaxBodyPreviewLength ? body : body[..MaxBodyPreviewLength],
            ApplicationPropertiesJson = JsonSerializer.Serialize(props),
            Status = DlqMessageStatus.Active,
            CorrelationId = $"corr-{ns.Id:N}-{i:D6}",
        };
    }

    private static (string Name, ServiceBusEntityType EntityType) BuildEntityIdentity(CloudProviderType provider, int index, Random rng)
    {
        var word = DomainWords[rng.Next(DomainWords.Length)];

        var wantsSubscription = provider switch
        {
            CloudProviderType.Gcp => true,
            CloudProviderType.Azure => rng.NextDouble() < 0.4,
            CloudProviderType.Aws => rng.NextDouble() < 0.15,
            _ => false,
        };

        if (!wantsSubscription)
        {
            return ($"{word}-{index:D4}", ServiceBusEntityType.Queue);
        }

        var topic = $"{word}-topic-{index:D4}";
        var fullName = provider == CloudProviderType.Azure
            ? $"{topic}/subscriptions/sub-{index:D4}"
            : $"{topic}/sub-{index:D4}";
        return (fullName, ServiceBusEntityType.Subscription);
    }

    private static string BuildFloodBody(Random rng, int i)
    {
        var roll = rng.NextDouble();
        return roll switch
        {
            < 0.55 => $"{{\"id\":\"{i:D6}\",\"value\":{rng.Next(1, 10000)},\"status\":\"pending\"}}",
            < 0.85 => "{\"id\":\"" + i.ToString("D6") + "\",\"payload\":\"" + new string('a', rng.Next(500, 4000)) + "\"}",
            < 0.96 => "{\"id\":\"" + i.ToString("D6") + "\",\"payload\":\"" + new string('b', rng.Next(20_000, 90_000)) + "\"}",
            _ => rng.Next(6) switch
            {
                0 => "{\"unterminated\":\"" + new string('x', 20_000),
                1 => "{\"customer\":\"Zoë Müller 日本語 🚀 café — naïve\",\"note\":\"emoji test \\ud83d\\ude00\"}",
                2 => "{\"id\":null,\"amount\":null,\"items\":[],\"nested\":{\"a\":null}}",
                3 => $"{{\"orderId\":\"ORD-{i}\",\"timestamp\":\"9999-12-31T23:59:59Z\",\"legacyTimestamp\":\"1970-01-01T00:00:00Z\"}}",
                4 => "not-json-at-all-plain-text-body-" + Guid.NewGuid(),
                _ => "{\"id\":\"" + i.ToString("D6") + "\",\"duplicateKey\":1,\"duplicateKey\":2}",
            },
        };
    }

    private static string Sanitize(string entityName) => entityName.Replace('/', '_');

    private static string RandomSuffix(Random rng) => rng.Next(100000, 999999).ToString();

    private static string BuildSyntheticConnectionString(CloudProviderType provider, string nsName) => provider switch
    {
        CloudProviderType.Aws => $"arn:aws:sqs:us-east-1:000000000000:{nsName}-flood-seed",
        CloudProviderType.Gcp => $"projects/servicehub-flood-test/topics/{nsName}-flood-seed",
        _ => $"Endpoint=sb://{nsName}-flood-seed.servicebus.windows.net/;SharedAccessKeyName=flood;SharedAccessKey=Zmxvb2Qtc2VlZC1ub3QtcmVhbA==",
    };

    private static string ComputeBodyHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>Parameters controlling <see cref="FloodSeedService.SeedAsync"/>'s generation volume.</summary>
public sealed record FloodSeedOptions(
    int NamespacesPerProvider,
    int MinEntitiesPerNamespace,
    int MaxEntitiesPerNamespace,
    int MinDlqPerEntity,
    int MaxDlqPerEntity,
    int MegaEntitiesPerNamespace,
    int MegaEntityDlqMax,
    int Seed);

/// <summary>Result summary from a <see cref="FloodSeedService.SeedAsync"/> call.</summary>
public sealed record FloodSeedResult(
    int NamespacesCreated,
    int EntitiesCreated,
    int DlqMessagesCreated,
    IReadOnlyList<Guid> NamespaceIds,
    TimeSpan Elapsed);
