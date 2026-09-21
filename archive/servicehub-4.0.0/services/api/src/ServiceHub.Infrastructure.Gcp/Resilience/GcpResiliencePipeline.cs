using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace ServiceHub.Infrastructure.Gcp.Resilience;

/// <summary>
/// Builds the Polly resilience pipeline used by the GCP Pub/Sub message receiver/sender.
/// Mirrors the Azure pipeline in <c>MessageReceiver</c>: 3 retries with exponential
/// backoff (1s base, 30s cap) plus jitter, retrying only transient gRPC status codes.
/// </summary>
internal static class GcpResiliencePipeline
{
    /// <summary>
    /// Creates a resilience pipeline that retries transient Pub/Sub gRPC errors.
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
                ShouldHandle = new PredicateBuilder().Handle<RpcException>(IsTransient),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry attempt {AttemptNumber} for GCP Pub/Sub operation after {Delay}ms. Exception: {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Treats the standard retryable gRPC status codes as transient.
    /// </summary>
    private static bool IsTransient(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.Internal
            or StatusCode.ResourceExhausted
            or StatusCode.Aborted;
}
