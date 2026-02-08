namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Runtime properties of a Service Bus queue.
/// </summary>
/// <param name="Name">The queue name.</param>
/// <param name="ActiveMessageCount">The number of active messages.</param>
/// <param name="DeadLetterMessageCount">The number of dead letter messages.</param>
/// <param name="ScheduledMessageCount">The number of scheduled messages.</param>
/// <param name="TransferMessageCount">The number of transfer messages.</param>
/// <param name="TransferDeadLetterMessageCount">The number of transfer dead letter messages.</param>
/// <param name="SizeInBytes">The size of the queue in bytes.</param>
/// <param name="Status">The entity status.</param>
/// <param name="CreatedAt">When the queue was created.</param>
/// <param name="UpdatedAt">When the queue was last updated.</param>
/// <param name="AccessedAt">When the queue was last accessed.</param>
/// <param name="RequiresSession">Whether sessions are required.</param>
/// <param name="RequiresDuplicateDetection">Whether duplicate detection is enabled.</param>
/// <param name="EnablePartitioning">Whether partitioning is enabled.</param>
/// <param name="EnableBatchedOperations">Whether batched operations are enabled.</param>
/// <param name="MaxSizeInMegabytes">The maximum size in megabytes.</param>
/// <param name="MaxDeliveryCount">The maximum delivery count.</param>
/// <param name="DefaultMessageTimeToLive">The default message time to live.</param>
/// <param name="LockDuration">The lock duration.</param>
/// <param name="AutoDeleteOnIdle">The auto-delete on idle duration.</param>
public sealed record QueueRuntimePropertiesDto(
    string Name,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long ScheduledMessageCount,
    long TransferMessageCount,
    long TransferDeadLetterMessageCount,
    long SizeInBytes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset AccessedAt,
    bool RequiresSession,
    bool RequiresDuplicateDetection,
    bool EnablePartitioning,
    bool EnableBatchedOperations,
    long MaxSizeInMegabytes,
    int MaxDeliveryCount,
    TimeSpan DefaultMessageTimeToLive,
    TimeSpan LockDuration,
    TimeSpan AutoDeleteOnIdle);

/// <summary>
/// Runtime properties of a Service Bus topic.
/// </summary>
/// <param name="Name">The topic name.</param>
/// <param name="SubscriptionCount">The number of subscriptions.</param>
/// <param name="SizeInBytes">The size in bytes.</param>
/// <param name="Status">The entity status.</param>
/// <param name="CreatedAt">When the topic was created.</param>
/// <param name="UpdatedAt">When the topic was last updated.</param>
/// <param name="AccessedAt">When the topic was last accessed.</param>
/// <param name="RequiresDuplicateDetection">Whether duplicate detection is enabled.</param>
/// <param name="EnablePartitioning">Whether partitioning is enabled.</param>
/// <param name="EnableBatchedOperations">Whether batched operations are enabled.</param>
/// <param name="SupportOrdering">Whether ordering is supported.</param>
/// <param name="MaxSizeInMegabytes">The maximum size in megabytes.</param>
/// <param name="DefaultMessageTimeToLive">The default message time to live.</param>
/// <param name="AutoDeleteOnIdle">The auto-delete on idle duration.</param>
/// <param name="DuplicateDetectionHistoryTimeWindow">The duplicate detection history time window.</param>
public sealed record TopicRuntimePropertiesDto(
    string Name,
    int SubscriptionCount,
    long SizeInBytes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset AccessedAt,
    bool RequiresDuplicateDetection,
    bool EnablePartitioning,
    bool EnableBatchedOperations,
    bool SupportOrdering,
    long MaxSizeInMegabytes,
    TimeSpan DefaultMessageTimeToLive,
    TimeSpan AutoDeleteOnIdle,
    TimeSpan DuplicateDetectionHistoryTimeWindow);

/// <summary>
/// Runtime properties of a Service Bus subscription.
/// </summary>
/// <param name="Name">The subscription name.</param>
/// <param name="TopicName">The parent topic name.</param>
/// <param name="ActiveMessageCount">The number of active messages.</param>
/// <param name="DeadLetterMessageCount">The number of dead letter messages.</param>
/// <param name="TransferMessageCount">The number of transfer messages.</param>
/// <param name="TransferDeadLetterMessageCount">The number of transfer dead letter messages.</param>
/// <param name="Status">The entity status.</param>
/// <param name="CreatedAt">When the subscription was created.</param>
/// <param name="UpdatedAt">When the subscription was last updated.</param>
/// <param name="AccessedAt">When the subscription was last accessed.</param>
/// <param name="RequiresSession">Whether sessions are required.</param>
/// <param name="EnableBatchedOperations">Whether batched operations are enabled.</param>
/// <param name="EnableDeadLetteringOnMessageExpiration">Whether dead lettering on expiration is enabled.</param>
/// <param name="EnableDeadLetteringOnFilterEvaluationExceptions">Whether dead lettering on filter exceptions is enabled.</param>
/// <param name="MaxDeliveryCount">The maximum delivery count.</param>
/// <param name="DefaultMessageTimeToLive">The default message time to live.</param>
/// <param name="LockDuration">The lock duration.</param>
/// <param name="AutoDeleteOnIdle">The auto-delete on idle duration.</param>
/// <param name="ForwardTo">The forward to entity.</param>
/// <param name="ForwardDeadLetteredMessagesTo">The forward dead lettered messages to entity.</param>
public sealed record SubscriptionRuntimePropertiesDto(
    string Name,
    string TopicName,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long TransferMessageCount,
    long TransferDeadLetterMessageCount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset AccessedAt,
    bool RequiresSession,
    bool EnableBatchedOperations,
    bool EnableDeadLetteringOnMessageExpiration,
    bool EnableDeadLetteringOnFilterEvaluationExceptions,
    int MaxDeliveryCount,
    TimeSpan DefaultMessageTimeToLive,
    TimeSpan LockDuration,
    TimeSpan AutoDeleteOnIdle,
    string? ForwardTo,
    string? ForwardDeadLetteredMessagesTo);
