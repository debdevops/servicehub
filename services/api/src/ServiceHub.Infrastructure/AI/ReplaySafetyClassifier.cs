using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Determines whether a DLQ message is safe to replay automatically
/// based on its failure category and delivery history.
/// </summary>
internal static class ReplaySafetyClassifier
{
    /// <summary>
    /// Returns one of the <see cref="ReplaySafetyLevels"/> values.
    /// </summary>
    internal static string Classify(DlqMessage msg, FailureCategory category)
    {
        // Transient failures are usually safe to retry
        if (category == FailureCategory.Transient)
            return msg.DeliveryCount <= 10 ? ReplaySafetyLevels.Safe : ReplaySafetyLevels.RequiresReview;

        // Expired messages cannot be replayed meaningfully
        if (category == FailureCategory.Expired)
            return ReplaySafetyLevels.Unsafe;

        // Auth failures need credential fixes first
        if (category == FailureCategory.Authorization)
            return ReplaySafetyLevels.Unsafe;

        // Data quality issues will fail again without a fix
        if (category == FailureCategory.DataQuality)
            return ReplaySafetyLevels.Unsafe;

        // Quota exceeded — depends on whether the quota was temporary
        if (category == FailureCategory.QuotaExceeded)
            return ReplaySafetyLevels.RequiresReview;

        // Max delivery — the message has already been retried beyond the limit
        if (category == FailureCategory.MaxDelivery)
            return msg.DeliveryCount <= 5 ? ReplaySafetyLevels.RequiresReview : ReplaySafetyLevels.Unsafe;

        // ResourceNotFound — might resolve if the resource is recreated
        if (category == FailureCategory.ResourceNotFound)
            return ReplaySafetyLevels.RequiresReview;

        // ProcessingError is ambiguous
        if (category == FailureCategory.ProcessingError)
            return ReplaySafetyLevels.RequiresReview;

        // Unknown — can't make a call
        return ReplaySafetyLevels.RequiresReview;
    }
}
