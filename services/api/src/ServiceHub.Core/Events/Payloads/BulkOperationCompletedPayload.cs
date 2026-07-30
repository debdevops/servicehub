using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.BulkOperationCompleted"/>.
/// Raised when a bulk replay/purge job (<see cref="Entities.BulkOperationJob"/>) reaches a
/// terminal status. Subscribers include <c>WebhookBulkOperationCompletedHandler</c>, which
/// bridges this to <see cref="Interfaces.IWebhookNotifier.NotifyBulkOperationCompletedAsync"/>.
/// </summary>
public sealed record BulkOperationCompletedPayload
{
    /// <summary>Identifier of the completed job.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Whether the job replayed or purged messages.</summary>
    public required BulkOperationType OperationType { get; init; }

    /// <summary>The job's terminal status.</summary>
    public required BulkOperationStatus Status { get; init; }

    /// <summary>Identifier of the namespace the job ran against.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Display name of the namespace the job ran against.</summary>
    public required string NamespaceName { get; init; }

    /// <summary>Total messages the job's filter matched at creation time.</summary>
    public required int TotalMatched { get; init; }

    /// <summary>Messages successfully processed.</summary>
    public required int SuccessCount { get; init; }

    /// <summary>Messages that failed.</summary>
    public required int FailureCount { get; init; }

    /// <summary>Messages skipped without an attempt.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>UTC timestamp when the job reached its terminal status.</summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }
}
