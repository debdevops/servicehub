using System.Text.Json.Serialization;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>
/// Flat JSON, no chat-platform envelope — the original (pre-Phase-2) webhook shape, preserved
/// exactly so upgrading ServiceHub never changes an existing integration's payload.
/// </summary>
public sealed class GenericWebhookFormatter : IWebhookMessageFormatter
{
    /// <inheritdoc />
    public WebhookFormat Format => WebhookFormat.Generic;

    /// <inheritdoc />
    public object BuildDlqSpikePayload(DlqSpikeNotification notification) => new DlqSpikePayload
    {
        NamespaceId = notification.NamespaceId,
        NamespaceName = notification.NamespaceName,
        NewMessageCount = notification.NewMessageCount,
        Threshold = notification.Threshold,
        DetectedAtUtc = notification.DetectedAtUtc,
    };

    /// <inheritdoc />
    public object BuildBulkOperationCompletedPayload(BulkOperationCompletedNotification notification) => new BulkOperationCompletedPayload
    {
        JobId = notification.JobId,
        OperationType = notification.OperationType.ToString(),
        Status = notification.Status.ToString(),
        NamespaceId = notification.NamespaceId,
        NamespaceName = notification.NamespaceName,
        TotalMatched = notification.TotalMatched,
        SuccessCount = notification.SuccessCount,
        FailureCount = notification.FailureCount,
        SkippedCount = notification.SkippedCount,
        CompletedAtUtc = notification.CompletedAtUtc,
    };

    /// <summary>Original DLQ-spike webhook payload shape — field-for-field identical to pre-Phase-2.</summary>
    internal sealed class DlqSpikePayload
    {
        [JsonPropertyName("namespaceId")]
        public Guid NamespaceId { get; init; }

        [JsonPropertyName("namespaceName")]
        public string NamespaceName { get; init; } = string.Empty;

        [JsonPropertyName("newMessageCount")]
        public int NewMessageCount { get; init; }

        [JsonPropertyName("threshold")]
        public int Threshold { get; init; }

        [JsonPropertyName("detectedAtUtc")]
        public DateTimeOffset DetectedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }

    /// <summary>Generic bulk-operation-completed webhook payload shape.</summary>
    internal sealed class BulkOperationCompletedPayload
    {
        [JsonPropertyName("jobId")]
        public Guid JobId { get; init; }

        [JsonPropertyName("operationType")]
        public string OperationType { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("namespaceId")]
        public Guid NamespaceId { get; init; }

        [JsonPropertyName("namespaceName")]
        public string NamespaceName { get; init; } = string.Empty;

        [JsonPropertyName("totalMatched")]
        public int TotalMatched { get; init; }

        [JsonPropertyName("successCount")]
        public int SuccessCount { get; init; }

        [JsonPropertyName("failureCount")]
        public int FailureCount { get; init; }

        [JsonPropertyName("skippedCount")]
        public int SkippedCount { get; init; }

        [JsonPropertyName("completedAtUtc")]
        public DateTimeOffset CompletedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }
}
