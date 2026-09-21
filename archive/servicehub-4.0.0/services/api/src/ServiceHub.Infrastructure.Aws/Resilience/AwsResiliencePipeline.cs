using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace ServiceHub.Infrastructure.Aws.Resilience;

/// <summary>
/// Builds the Polly resilience pipeline used by the AWS message receiver/sender.
/// Mirrors the Azure pipeline in <c>MessageReceiver</c>: 3 retries with exponential
/// backoff (1s base, 30s cap) plus jitter, retrying only transient SQS/SNS errors.
/// </summary>
internal static class AwsResiliencePipeline
{
    /// <summary>
    /// Creates a resilience pipeline that retries transient AWS service exceptions.
    /// </summary>
    /// <param name="logger">Logger used to record retry attempts.</param>
    public static ResiliencePipeline Create(ILogger logger)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<AmazonServiceException>(IsTransient),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry attempt {AttemptNumber} for AWS SQS/SNS operation after {Delay}ms. Exception: {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Treats AWS service exceptions as transient when the SDK marks them retryable,
    /// or when the HTTP status indicates a server-side/throttling condition.
    /// </summary>
    private static bool IsTransient(AmazonServiceException ex) =>
        ex.Retryable is not null
        || (int)ex.StatusCode >= 500
        || (int)ex.StatusCode == 429;
}
