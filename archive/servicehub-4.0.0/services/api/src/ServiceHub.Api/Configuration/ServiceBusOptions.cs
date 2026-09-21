using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Strongly-typed view of the "ServiceBus" configuration section, used for startup
/// validation. The runtime client factory still reads these keys directly from
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>; binding them here as
/// well lets ServiceHub fail fast with a clear message when an operator supplies an
/// out-of-range value, instead of surfacing an opaque error at first message peek.
/// </summary>
public sealed class ServiceBusOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ServiceBus";

    /// <summary>How long a cached Service Bus client is retained. Must be at least 1 minute.</summary>
    [Range(1, 1440, ErrorMessage = "ServiceBus:ConnectionCacheExpirationMinutes must be between 1 and 1440.")]
    public int ConnectionCacheExpirationMinutes { get; set; } = 60;

    /// <summary>Maximum concurrent receive calls. Must be at least 1.</summary>
    [Range(1, 1000, ErrorMessage = "ServiceBus:MaxConcurrentCalls must be between 1 and 1000.")]
    public int MaxConcurrentCalls { get; set; } = 10;

    /// <summary>Prefetch count for the receiver. Zero disables prefetch.</summary>
    [Range(0, 10000, ErrorMessage = "ServiceBus:PrefetchCount must be between 0 and 10000.")]
    public int PrefetchCount { get; set; } = 100;

    /// <summary>Number of automatic retries. Non-negative.</summary>
    [Range(0, 20, ErrorMessage = "ServiceBus:RetryCount must be between 0 and 20.")]
    public int RetryCount { get; set; } = 3;

    /// <summary>Base retry delay in milliseconds. Non-negative.</summary>
    [Range(0, 600000, ErrorMessage = "ServiceBus:RetryDelayMs must be between 0 and 600000.")]
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Maximum retry delay in milliseconds. Non-negative.</summary>
    [Range(0, 600000, ErrorMessage = "ServiceBus:MaxRetryDelayMs must be between 0 and 600000.")]
    public int MaxRetryDelayMs { get; set; } = 30000;
}
