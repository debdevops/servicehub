using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Determines whether a DLQ message is safe to replay automatically
/// based on its failure category and delivery history.
/// </summary>
internal static class ReplaySafetyClassifier
{
    internal const string Safe = "Safe";
    internal const string Unsafe = "Unsafe";
    internal const string RequiresReview = "RequiresReview";

    /// <summary>
    /// Returns <c>Safe</c>, <c>Unsafe</c>, or <c>RequiresReview</c>.
    /// </summary>
    internal static string Classify(DlqMessage msg, FailureCategory category)
    {
        // Transient failures are usually safe to retry
        if (category == FailureCategory.Transient)
            return msg.DeliveryCount <= 10 ? Safe : RequiresReview;

        // Expired messages cannot be replayed meaningfully
        if (category == FailureCategory.Expired)
            return Unsafe;

        // Auth failures need credential fixes first
        if (category == FailureCategory.Authorization)
            return Unsafe;

        // Data quality issues will fail again without a fix
        if (category == FailureCategory.DataQuality)
            return Unsafe;

        // Quota exceeded — depends on whether the quota was temporary
        if (category == FailureCategory.QuotaExceeded)
            return RequiresReview;

        // Max delivery — the message has already been retried beyond the limit
        if (category == FailureCategory.MaxDelivery)
            return msg.DeliveryCount <= 5 ? RequiresReview : Unsafe;

        // ResourceNotFound — might resolve if the resource is recreated
        if (category == FailureCategory.ResourceNotFound)
            return RequiresReview;

        // ProcessingError is ambiguous
        if (category == FailureCategory.ProcessingError)
            return RequiresReview;

        // Unknown — can't make a call
        return RequiresReview;
    }
}
