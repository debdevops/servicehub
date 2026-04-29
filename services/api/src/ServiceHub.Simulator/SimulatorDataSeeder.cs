using System.Reflection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// Seeds the <see cref="ISimulatorStore"/> with deterministic, realistic data for
/// all three cloud providers (Azure, AWS, GCP).
/// <para>
/// Fixed GUIDs are used so integration tests can reference known namespace IDs
/// without any database lookups.  All dead-letter message strings exactly match
/// the patterns expected by <c>DeterministicClassifier</c> in
/// <c>ServiceHub.Infrastructure.AI</c>.
/// </para>
/// </summary>
public sealed class SimulatorDataSeeder
{
    // ── Well-known namespace GUIDs ────────────────────────────────────────────

    /// <summary>Fixed GUID for the simulated Azure Service Bus namespace.</summary>
    public static readonly Guid AzureNamespaceId = new("a1b2c3d4-0001-0001-0001-000000000001");

    /// <summary>Fixed GUID for the simulated AWS SQS namespace.</summary>
    public static readonly Guid AwsNamespaceId = new("b2c3d4e5-0002-0002-0002-000000000002");

    /// <summary>Fixed GUID for the simulated GCP Pub/Sub namespace.</summary>
    public static readonly Guid GcpNamespaceId = new("c3d4e5f6-0003-0003-0003-000000000003");

    private readonly ISimulatorStore _store;
    private readonly INamespaceRepository _namespaceRepo;

    /// <summary>Initializes a new instance of <see cref="SimulatorDataSeeder"/>.</summary>
    public SimulatorDataSeeder(ISimulatorStore store, INamespaceRepository namespaceRepo)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _namespaceRepo = namespaceRepo ?? throw new ArgumentNullException(nameof(namespaceRepo));
    }

    /// <summary>
    /// Registers all simulator namespaces and entities, then seeds messages.
    /// Safe to call multiple times — <see cref="ISimulatorStore.Reset"/> clears
    /// messages but leaves structure intact.
    /// </summary>
    public void Seed()
    {
        _store.Purge();
        RegisterNamespaces();
        SeedAzure();
        SeedAws();
        SeedGcp();
    }

    // ── Namespace registration ────────────────────────────────────────────────

    private void RegisterNamespaces()
    {
        _store.RegisterNamespace(AzureNamespaceId,
            "sim-azure-servicebus", "Simulated Azure Service Bus", CloudProviderType.Azure);
        _store.RegisterNamespace(AwsNamespaceId,
            "sim-aws-sqs", "Simulated AWS SQS", CloudProviderType.Aws);
        _store.RegisterNamespace(GcpNamespaceId,
            "sim-gcp-pubsub", "Simulated GCP Pub/Sub", CloudProviderType.Gcp);

        // Register the fixed-ID simulator namespaces in the shared INamespaceRepository
        // so that MessagesController can resolve them via the standard namespace lookup path.
        // Dummy connection strings are used — the simulator providers never use them.
        EnsureInRepository(AzureNamespaceId,
            "sim-azure-servicebus", "Simulated Azure Service Bus",
            "Endpoint=sb://sim-azure.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            CloudProviderType.Azure);

        EnsureInRepository(AwsNamespaceId,
            "sim-aws-sqs", "Simulated AWS SQS",
            "aws://AKIASIMULATORTEST00001/us-east-1",
            CloudProviderType.Aws, awsRegion: "us-east-1");

        EnsureInRepository(GcpNamespaceId,
            "sim-gcp-pubsub", "Simulated GCP Pub/Sub",
            "gcp://simulator-project/sim-gcp-pubsub",
            CloudProviderType.Gcp, gcpProjectId: "simulator-project");
    }

    private void EnsureInRepository(
        Guid id, string name, string displayName, string connectionString,
        CloudProviderType provider,
        string? awsRegion = null, string? gcpProjectId = null)
    {
        // Check whether the fixed-ID namespace already exists (re-entrant Seed() call).
        var existing = _namespaceRepo.GetByIdAsync(id).GetAwaiter().GetResult();
        if (existing.IsSuccess)
        {
            return;
        }

        var createResult = Namespace.Create(
            name, connectionString, displayName,
            provider: provider,
            awsRegion: awsRegion,
            gcpProjectId: gcpProjectId);

        if (createResult.IsFailure)
        {
            return; // should never happen with the fixed strings above
        }

        var ns = createResult.Value;
        SetPrivateProperty(ns, nameof(Namespace.Id), id);

        _namespaceRepo.AddAsync(ns).GetAwaiter().GetResult();
    }

    private static void SetPrivateProperty<T>(Namespace target, string propertyName, T value)
    {
        var property = typeof(Namespace).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property?.SetValue(target, value);
    }

    // ── Azure seeding ─────────────────────────────────────────────────────────

    private void SeedAzure()
    {
        // orders queue — 20 active + 5 DLQ
        var orders = CreateAzureEntity("orders", "Queue");
        _store.RegisterEntity(orders, AzureNamespaceId);
        AddActiveMessages(orders, 20, "order");
        AddDlqMessage(orders, "MaxDeliveryCountExceeded",
            "MaxDeliveryCountExceeded: message exceeded max delivery count of 10",
            deliveryCount: 10);
        AddDlqMessage(orders, "TTLExpiredException",
            "TTLExpiredException: message TTL expired before processing",
            deliveryCount: 3);
        AddDlqMessage(orders, "ApplicationError",
            "JsonException: unexpected token at position 42 — invalid JSON body",
            deliveryCount: 2);
        AddDlqMessage(orders, "ApplicationError",
            "401 Unauthorized — payment gateway rejected token",
            deliveryCount: 1);
        AddDlqMessage(orders, "ApplicationError",
            "SQL query timeout — connection refused after 30s",
            deliveryCount: 5);

        // payments queue — 15 active + 4 DLQ
        var payments = CreateAzureEntity("payments", "Queue");
        _store.RegisterEntity(payments, AzureNamespaceId);
        AddActiveMessages(payments, 15, "payment");
        AddDlqMessage(payments, "MaxDeliveryCountExceeded",
            "MaxDeliveryCountExceeded: consumer could not process after 10 attempts",
            deliveryCount: 10);
        AddDlqMessage(payments, "ApplicationError",
            "JsonException: unexpected character after JSON — malformed payload",
            deliveryCount: 2);
        AddDlqMessage(payments, "ApplicationError",
            "401 Unauthorized — stripe API rejected authorization header",
            deliveryCount: 1);
        AddDlqMessage(payments, "ApplicationError",
            "ProcessingError: downstream service returned 500",
            deliveryCount: 3);

        // notifications queue — 8 active + 1 DLQ
        var notifications = CreateAzureEntity("notifications", "Queue");
        _store.RegisterEntity(notifications, AzureNamespaceId);
        AddActiveMessages(notifications, 8, "notification");
        AddDlqMessage(notifications, "TTLExpiredException",
            "TTLExpiredException: notification expired — no longer relevant",
            deliveryCount: 1);

        // inventory-events topic / stock-service subscription
        var inventoryTopic = CreateAzureEntity("inventory-events", "Topic");
        _store.RegisterEntity(inventoryTopic, AzureNamespaceId);

        var stockSub = CreateAzureEntity("inventory-events/subscriptions/stock-service", "Subscription");
        _store.RegisterEntity(stockSub, AzureNamespaceId);
        AddActiveMessages(stockSub, 12, "inventory");
        AddDlqMessage(stockSub, "MaxDeliveryCountExceeded",
            "MaxDeliveryCountExceeded: stock service could not process message",
            deliveryCount: 10);

        // user-events topic / analytics-sub + audit-sub
        var userEventsTopic = CreateAzureEntity("user-events", "Topic");
        _store.RegisterEntity(userEventsTopic, AzureNamespaceId);

        var analyticsSub = CreateAzureEntity("user-events/subscriptions/analytics-sub", "Subscription");
        _store.RegisterEntity(analyticsSub, AzureNamespaceId);
        AddActiveMessages(analyticsSub, 30, "user-event");

        var auditSub = CreateAzureEntity("user-events/subscriptions/audit-sub", "Subscription");
        _store.RegisterEntity(auditSub, AzureNamespaceId);
        AddActiveMessages(auditSub, 30, "user-event");
        AddDlqMessage(auditSub, "ApplicationError",
            "JsonException: unexpected token at position 0 — empty body",
            deliveryCount: 2);
    }

    // ── AWS seeding ───────────────────────────────────────────────────────────

    private void SeedAws()
    {
        // checkout-queue — 18 active + 3 DLQ
        var checkout = new SimulatorEntity
        {
            Name = "checkout-queue",
            EntityType = "Queue",
            Provider = CloudProviderType.Aws,
            VisibilityTimeoutSeconds = 30,
            MaxDeliveryAttempts = 5,
        };
        _store.RegisterEntity(checkout, AwsNamespaceId);
        AddActiveMessages(checkout, 18, "checkout");

        // AWS DLQ reason "MaxReceiveCount" — falls through to heuristics → MaxDelivery
        AddDlqMessageWithProperties(checkout, "MaxReceiveCount",
            "Message exceeded MaxReceiveCount",
            deliveryCount: 5,
            props: new Dictionary<string, object>());
        AddDlqMessage(checkout, "MaxReceiveCount",
            "MaxReceiveCount exceeded — consumer lambda crashed during processing",
            deliveryCount: 5);
        AddDlqMessageWithProperties(checkout, "ApplicationError",
            "Lambda function threw unhandled exception",
            deliveryCount: 2,
            props: new Dictionary<string, object> { ["ErrorCode"] = "LambdaError" });

        // inventory-updates-queue — 25 active
        var inventory = new SimulatorEntity
        {
            Name = "inventory-updates-queue",
            EntityType = "Queue",
            Provider = CloudProviderType.Aws,
            VisibilityTimeoutSeconds = 60,
        };
        _store.RegisterEntity(inventory, AwsNamespaceId);
        AddActiveMessages(inventory, 25, "inventory");

        // notifications-queue — 10 active
        var notifications = new SimulatorEntity
        {
            Name = "notifications-queue",
            EntityType = "Queue",
            Provider = CloudProviderType.Aws,
            VisibilityTimeoutSeconds = 20,
        };
        _store.RegisterEntity(notifications, AwsNamespaceId);
        AddActiveMessages(notifications, 10, "notification");
    }

    // ── GCP seeding ───────────────────────────────────────────────────────────

    private void SeedGcp()
    {
        // fulfillment-sub — 22 active + 3 DLQ
        var fulfillment = new SimulatorEntity
        {
            Name = "fulfillment-sub",
            EntityType = "Subscription",
            Provider = CloudProviderType.Gcp,
            AckDeadlineSeconds = 20,
            DeadLetterTopicName = "fulfillment-dlq",
            MaxDeliveryAttempts = 5,
            MessageOrderingEnabled = false,
        };
        _store.RegisterEntity(fulfillment, GcpNamespaceId);
        AddActiveMessages(fulfillment, 22, "fulfillment");

        // GCP: googclient_deliveryattempt >= MaxDeliveryAttempts → MaxDelivery via heuristics
        AddDlqMessageWithProperties(fulfillment, "MaxDeliveryAttempts",
            "Exceeded max delivery attempts",
            deliveryCount: 5,
            props: new Dictionary<string, object> { ["googclient_deliveryattempt"] = "5" });
        AddDlqMessageWithProperties(fulfillment, "MaxDeliveryAttempts",
            "Subscriber could not process after 5 attempts",
            deliveryCount: 5,
            props: new Dictionary<string, object> { ["googclient_deliveryattempt"] = "5" });
        AddDlqMessage(fulfillment, "nack",
            "Subscriber explicitly nacked the message",
            deliveryCount: 3);

        // analytics-sub — 40 active (ordering enabled)
        var analytics = new SimulatorEntity
        {
            Name = "analytics-sub",
            EntityType = "Subscription",
            Provider = CloudProviderType.Gcp,
            AckDeadlineSeconds = 10,
            MessageOrderingEnabled = true,
        };
        _store.RegisterEntity(analytics, GcpNamespaceId);
        AddActiveMessages(analytics, 40, "analytics");

        // audit-log-sub — 15 active
        var auditLog = new SimulatorEntity
        {
            Name = "audit-log-sub",
            EntityType = "Subscription",
            Provider = CloudProviderType.Gcp,
            AckDeadlineSeconds = 60,
            DeadLetterTopicName = "audit-dlq",
            MaxDeliveryAttempts = 10,
        };
        _store.RegisterEntity(auditLog, GcpNamespaceId);
        AddActiveMessages(auditLog, 15, "audit");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimulatorEntity CreateAzureEntity(string name, string entityType) =>
        new()
        {
            Name = name,
            EntityType = entityType,
            Provider = CloudProviderType.Azure,
            MaxDeliveryAttempts = 10,
            VisibilityTimeoutSeconds = 0,
        };

    private static void AddActiveMessages(SimulatorEntity entity, int count, string prefix)
    {
        for (var i = 1; i <= count; i++)
        {
            var seq = entity.NextSequenceNumber();
            entity.EnqueueMessage(new SimulatorMessage(
                MessageId: $"{prefix}-{seq:D6}",
                SequenceNumber: seq,
                Body: $$$"""{"id":"{{{prefix}}}-{{{seq}}}","timestamp":"{{{DateTimeOffset.UtcNow:O}}}","data":{"value":{{{i}}}}}""",
                ContentType: "application/json",
                CorrelationId: $"corr-{Guid.NewGuid():N}",
                SessionId: null,
                PartitionKey: null,
                Subject: $"{prefix}.created",
                DeliveryCount: 0,
                EnqueuedTime: DateTimeOffset.UtcNow.AddMinutes(-count + i),
                ScheduledEnqueueTime: null,
                IsDeadLettered: false,
                DeadLetterReason: null,
                DeadLetterErrorDescription: null,
                ApplicationProperties: new Dictionary<string, object>
                {
                    ["MessageType"] = $"{prefix}.v1",
                    ["Source"] = "simulator",
                },
                SizeInBytes: 128 + i * 4,
                ReceiptHandle: null,
                VisibilityUntil: null,
                OrderingKey: null,
                DeliveryAttempt: 0,
                AckDeadline: null,
                IsNacked: false,
                Provider: entity.Provider));
        }
    }

    private static void AddDlqMessage(
        SimulatorEntity entity, string reason, string description, int deliveryCount)
    {
        AddDlqMessageWithProperties(entity, reason, description, deliveryCount,
            new Dictionary<string, object>());
    }

    private static void AddDlqMessageWithProperties(
        SimulatorEntity entity, string reason, string description, int deliveryCount,
        Dictionary<string, object> props)
    {
        var seq = entity.NextSequenceNumber();
        entity.EnqueueDlqMessage(new SimulatorMessage(
            MessageId: $"dlq-{entity.Name}-{seq:D6}",
            SequenceNumber: seq,
            Body: $$$"""{"id":"dlq-{{{seq}}}","error":"{{{description.Replace("\"", "\\\"")}}}"}""",
            ContentType: "application/json",
            CorrelationId: $"corr-{Guid.NewGuid():N}",
            SessionId: null,
            PartitionKey: null,
            Subject: null,
            DeliveryCount: deliveryCount,
            EnqueuedTime: DateTimeOffset.UtcNow.AddHours(-1),
            ScheduledEnqueueTime: null,
            IsDeadLettered: true,
            DeadLetterReason: reason,
            DeadLetterErrorDescription: description,
            ApplicationProperties: props,
            SizeInBytes: 256,
            ReceiptHandle: null,
            VisibilityUntil: null,
            OrderingKey: null,
            DeliveryAttempt: deliveryCount,
            AckDeadline: null,
            IsNacked: false,
            Provider: entity.Provider));
    }
}
