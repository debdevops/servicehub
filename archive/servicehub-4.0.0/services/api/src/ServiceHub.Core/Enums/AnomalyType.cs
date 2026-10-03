namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the type of anomaly detected in message patterns or system behavior.
/// Used for monitoring and alerting purposes.
/// </summary>
public enum AnomalyType
{
    /// <summary>
    /// No anomaly detected.
    /// </summary>
    None = 0,

    /// <summary>
    /// High volume of messages detected in a short time period.
    /// </summary>
    HighMessageVolume = 1,

    /// <summary>
    /// Unusually low message throughput detected.
    /// </summary>
    LowMessageVolume = 2,

    /// <summary>
    /// High rate of message failures or dead-lettering.
    /// </summary>
    HighFailureRate = 3,

    /// <summary>
    /// Messages are taking longer than expected to process.
    /// </summary>
    SlowProcessing = 4,

    /// <summary>
    /// Queue or subscription depth is growing abnormally.
    /// </summary>
    QueueBacklog = 5,

    /// <summary>
    /// Dead-letter queue has exceeded threshold.
    /// </summary>
    DeadLetterThresholdExceeded = 6,

    /// <summary>
    /// Connection issues or intermittent failures detected.
    /// </summary>
    ConnectivityIssues = 7,

    /// <summary>
    /// Unusual pattern in message size distribution.
    /// </summary>
    UnusualMessageSize = 8,

    /// <summary>
    /// Duplicate messages detected.
    /// </summary>
    DuplicateMessages = 9,

    /// <summary>
    /// Messages with expired TTL detected.
    /// </summary>
    ExpiredMessages = 10
}
