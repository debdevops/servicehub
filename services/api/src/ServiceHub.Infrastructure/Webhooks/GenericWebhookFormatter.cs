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

    /// <inheritdoc />
    public object BuildAutonomyTransitionPayload(AutonomyTransitionNotification notification) => new AutonomyTransitionPayload
    {
        SignatureHash = notification.SignatureHash,
        PreviousLevel = notification.PreviousLevel.ToString(),
        NewLevel = notification.NewLevel.ToString(),
        Reason = notification.Reason,
        TransitionedAtUtc = notification.TransitionedAtUtc,
    };

    /// <inheritdoc />
    public object BuildCircuitBreakerTrippedPayload(CircuitBreakerTrippedNotification notification) => new CircuitBreakerTrippedPayload
    {
        RuleId = notification.RuleId,
        RuleName = notification.RuleName,
        SampleSize = notification.SampleSize,
        VerifiedSuccessRate = notification.VerifiedSuccessRate,
        TrippedAtUtc = notification.TrippedAtUtc,
    };

    /// <inheritdoc />
    public object BuildInsightDetectedPayload(InsightDetectedNotification notification) => new InsightDetectedPayload
    {
        Kind = notification.Kind.ToString(),
        FindingId = notification.FindingId,
        NamespaceId = notification.NamespaceId,
        NamespaceName = notification.NamespaceName,
        EntityName = notification.EntityName,
        Description = notification.Description,
        Severity = notification.Severity,
        DetectedAtUtc = notification.DetectedAtUtc,
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

    /// <summary>Generic autonomy-grant-transition webhook payload shape.</summary>
    internal sealed class AutonomyTransitionPayload
    {
        [JsonPropertyName("signatureHash")]
        public string SignatureHash { get; init; } = string.Empty;

        [JsonPropertyName("previousLevel")]
        public string PreviousLevel { get; init; } = string.Empty;

        [JsonPropertyName("newLevel")]
        public string NewLevel { get; init; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;

        [JsonPropertyName("transitionedAtUtc")]
        public DateTimeOffset TransitionedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }

    /// <summary>Generic circuit-breaker-tripped webhook payload shape.</summary>
    internal sealed class CircuitBreakerTrippedPayload
    {
        [JsonPropertyName("ruleId")]
        public long RuleId { get; init; }

        [JsonPropertyName("ruleName")]
        public string RuleName { get; init; } = string.Empty;

        [JsonPropertyName("sampleSize")]
        public int SampleSize { get; init; }

        [JsonPropertyName("verifiedSuccessRate")]
        public double VerifiedSuccessRate { get; init; }

        [JsonPropertyName("trippedAtUtc")]
        public DateTimeOffset TrippedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }

    /// <summary>Generic insight-detected webhook payload shape.</summary>
    internal sealed class InsightDetectedPayload
    {
        [JsonPropertyName("kind")]
        public string Kind { get; init; } = string.Empty;

        [JsonPropertyName("findingId")]
        public Guid FindingId { get; init; }

        [JsonPropertyName("namespaceId")]
        public Guid? NamespaceId { get; init; }

        [JsonPropertyName("namespaceName")]
        public string? NamespaceName { get; init; }

        [JsonPropertyName("entityName")]
        public string? EntityName { get; init; }

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("severity")]
        public int Severity { get; init; }

        [JsonPropertyName("detectedAtUtc")]
        public DateTimeOffset DetectedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }
}
