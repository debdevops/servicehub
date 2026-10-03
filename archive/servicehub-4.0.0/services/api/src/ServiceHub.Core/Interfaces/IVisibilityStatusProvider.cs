using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Provides AWS SQS-specific visibility-window status.
/// Implemented by both <c>AwsMessageReceiver</c> and <c>SimulatedAwsReceiver</c>.
/// </summary>
public interface IVisibilityStatusProvider
{
    /// <summary>
    /// Returns a snapshot of in-flight and dead-letter message counts for the given SQS queue.
    /// </summary>
    /// <param name="namespaceId">The AWS account/namespace identifier.</param>
    /// <param name="queueName">The SQS queue name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<SqsVisibilityInfo>> GetVisibilityWindowStatusAsync(
        Guid namespaceId, string queueName, CancellationToken cancellationToken = default);
}
