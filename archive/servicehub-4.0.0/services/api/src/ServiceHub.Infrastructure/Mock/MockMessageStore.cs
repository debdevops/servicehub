using System.Collections.Concurrent;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// Thread-safe in-memory store that seeds realistic messages for all mock namespaces.
/// Registered as a singleton so seeded data persists for the lifetime of the app.
/// </summary>
public sealed class MockMessageStore
{
    // key: (namespaceId, entityName, isDlq) → list of messages
    private readonly ConcurrentDictionary<(Guid, string, bool), List<Message>> _messages = new();
    private readonly ConcurrentQueue<SendMessageRequest> _sentMessages = new();
    private long _sequenceCounter = 1000;

    public MockMessageStore()
    {
        SeedAll();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public IEnumerable<Message> GetMessages(Guid namespaceId, string entityName, bool dlq)
        => _messages.TryGetValue((namespaceId, entityName, dlq), out var list)
            ? list
            : [];

    public void AddSentMessage(SendMessageRequest request)
        => _sentMessages.Enqueue(request);

    public IReadOnlyList<SendMessageRequest> GetSentMessages()
        => [.. _sentMessages];

    // ── Seeding ───────────────────────────────────────────────────────────────

    private void SeedAll()
    {
        var mockNamespaceId = MockNamespaces.MockNamespaceId;

        // Azure Service Bus – Contoso Commerce
        Seed(mockNamespaceId, "orders", dlq: false, GenerateAzureOrders(40));
        Seed(mockNamespaceId, "orders", dlq: true, GenerateAzureDlq("orders", 8));
        Seed(mockNamespaceId, "payments", dlq: false, GenerateAzurePayments(30));
        Seed(mockNamespaceId, "payments", dlq: true, GenerateAzureDlq("payments", 5));
        Seed(mockNamespaceId, "notifications", dlq: false, GenerateSimple(mockNamespaceId, "notifications", 20));
        Seed(mockNamespaceId, "inventory-sync", dlq: false, GenerateSimple(mockNamespaceId, "inventory-sync", 25));

        // AWS SQS – AcmeRetail
        var awsId = MockNamespaces.AwsMockNamespaceId;
        Seed(awsId, "order-processing", dlq: false, GenerateAwsOrders(35));
        Seed(awsId, "order-processing", dlq: true, GenerateAzureDlq("order-processing", 6));
        Seed(awsId, "payment-gateway-events", dlq: false, GenerateSimple(awsId, "payment-gateway-events", 20));
        Seed(awsId, "notification-service", dlq: false, GenerateSimple(awsId, "notification-service", 15));
        Seed(awsId, "fraud-detection", dlq: false, GenerateSimple(awsId, "fraud-detection", 12));

        // GCP Pub/Sub – MedStream Healthcare
        var gcpId = MockNamespaces.GcpMockNamespaceId;
        Seed(gcpId, "patient-intake", dlq: false, GenerateSimple(gcpId, "patient-intake", 30));
        Seed(gcpId, "lab-results", dlq: false, GenerateSimple(gcpId, "lab-results", 22));
        Seed(gcpId, "billing-events", dlq: false, GenerateSimple(gcpId, "billing-events", 18));
        Seed(gcpId, "clinical-alerts", dlq: true, GenerateAzureDlq("clinical-alerts", 4));
    }

    private void Seed(Guid ns, string entity, bool dlq, IEnumerable<Message> msgs)
        => _messages[(ns, entity, dlq)] = [.. msgs];

    // ── Azure message generators ──────────────────────────────────────────────

    private IEnumerable<Message> GenerateAzureOrders(int count)
    {
        var statuses = new[] { "Pending", "Processing", "Shipped", "Delivered" };
        return Enumerable.Range(1, count).Select(i => new Message
        {
            MessageId = $"azure-order-{i:D4}",
            SequenceNumber = NextSeq(),
            Body = $"{{\"orderId\":\"ORD-{i:D6}\",\"customer\":\"customer-{i % 50}\",\"status\":\"{statuses[i % statuses.Length]}\",\"total\":{(i * 37.99):F2},\"items\":{(i % 5) + 1}}}",
            ContentType = "application/json",
            Subject = $"Order.Created.v1",
            CorrelationId = $"corr-{i:D6}",
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-(count - i) * 3),
            DeliveryCount = 1,
            State = MessageState.Active,
            ApplicationProperties = new Dictionary<string, object>
            {
                ["source"] = "contoso-commerce",
                ["version"] = "1.0",
                ["region"] = "eastus"
            }
        });
    }

    private IEnumerable<Message> GenerateAzurePayments(int count)
        => Enumerable.Range(1, count).Select(i => new Message
        {
            MessageId = $"azure-payment-{i:D4}",
            SequenceNumber = NextSeq(),
            Body = BuildPaymentBody(i),
            ContentType = "application/json",
            Subject = "Payment.Processed.v1",
            CorrelationId = $"corr-{i:D6}",
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-(count - i) * 5),
            DeliveryCount = 1,
            State = MessageState.Active,
            ApplicationProperties = new Dictionary<string, object> { ["source"] = "payment-service" }
        });

    private static string BuildPaymentBody(int i)
    {
        var method = i % 3 == 0 ? "card" : i % 3 == 1 ? "paypal" : "bank";
        return $"{{\"paymentId\":\"PAY-{i:D6}\",\"orderId\":\"ORD-{i:D6}\",\"amount\":{(i * 49.99):F2},\"currency\":\"USD\",\"method\":\"{method}\"}}";
    }

    private IEnumerable<Message> GenerateAzureDlq(string entity, int count)
        => Enumerable.Range(1, count).Select(i => new Message
        {
            MessageId = $"dlq-{entity}-{i:D4}",
            SequenceNumber = NextSeq(),
            Body = $"{{\"error\":\"Validation failed\",\"entityId\":\"{i:D6}\",\"retries\":3}}",
            ContentType = "application/json",
            Subject = "DeadLettered",
            EnqueuedTime = DateTimeOffset.UtcNow.AddHours(-i),
            DeliveryCount = 3,
            State = MessageState.Active,
            DeadLetterSource = entity,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = $"Message exceeded max delivery count after {3 + i % 2} attempts",
            ApplicationProperties = new Dictionary<string, object> { ["dlq"] = "true" }
        });

    // ── AWS SQS generators ────────────────────────────────────────────────────

    private IEnumerable<Message> GenerateAwsOrders(int count)
        => Enumerable.Range(1, count).Select(i => new Message
        {
            MessageId = $"aws-{Guid.NewGuid():N}",
            SequenceNumber = NextSeq(),
            Body = BuildAwsOrderBody(i),
            ContentType = "application/json",
            Subject = "OrderCreated",
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-(count - i) * 2),
            DeliveryCount = 1,
            State = MessageState.Active,
            ApplicationProperties = new Dictionary<string, object>
            {
                ["awsRegion"] = "us-east-1",
                ["sqsQueue"] = "order-processing",
                ["receiptHandle"] = $"receipt-{i:D8}"
            }
        });

    private static string BuildAwsOrderBody(int i)
    {
        var innerJson = $"{{\"orderId\":\"ACM-{i:D5}\",\"store\":\"acmeretail\",\"total\":{(i * 29.99):F2}}}";
        return $"{{\"Type\":\"Order\",\"MessageId\":\"aws-order-{i}\",\"Timestamp\":\"{DateTimeOffset.UtcNow.AddMinutes(-i):O}\",\"Message\":\"{innerJson}\"}}";
    }

    // ── Generic generator for remaining entities ──────────────────────────────

    private IEnumerable<Message> GenerateSimple(Guid ns, string entity, int count)
        => Enumerable.Range(1, count).Select(i => new Message
        {
            MessageId = $"{ns.ToString()[..8]}-{entity}-{i:D4}",
            SequenceNumber = NextSeq(),
            Body = $"{{\"id\":\"{i:D6}\",\"entity\":\"{entity}\",\"payload\":\"mock-data-{i}\",\"timestamp\":\"{DateTimeOffset.UtcNow.AddMinutes(-i):O}\"}}",
            ContentType = "application/json",
            Subject = $"{entity}.Event.v1",
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-(count - i) * 4),
            DeliveryCount = 1,
            State = MessageState.Active,
            ApplicationProperties = new Dictionary<string, object> { ["source"] = entity }
        });

    private long NextSeq() => Interlocked.Increment(ref _sequenceCounter);
}
