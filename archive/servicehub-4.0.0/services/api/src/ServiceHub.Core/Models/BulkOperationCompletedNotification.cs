using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// The information an <see cref="Interfaces.IWebhookMessageFormatter"/> needs to render a bulk
/// operation completion alert — the notification counterpart to a finished
/// <see cref="Entities.BulkOperationJob"/>.
/// </summary>
public sealed record BulkOperationCompletedNotification(
    Guid JobId,
    BulkOperationType OperationType,
    BulkOperationStatus Status,
    Guid NamespaceId,
    string NamespaceName,
    int TotalMatched,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    DateTimeOffset CompletedAtUtc,
    string? InvestigateUrl);
