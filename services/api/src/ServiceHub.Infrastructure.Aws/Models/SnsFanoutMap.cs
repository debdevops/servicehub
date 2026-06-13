using ServiceHub.Infrastructure.Aws.Models;

namespace ServiceHub.Infrastructure.Aws.Models;

/// <summary>
/// Represents the fanout topology of an SNS topic showing all subscriptions and their statuses.
/// Use this to diagnose missing message deliveries: a subscription in <c>PendingConfirmation</c>
/// state silently drops all messages sent to the topic.
/// </summary>
/// <param name="TopicArn">The full ARN of the SNS topic.</param>
/// <param name="Subscriptions">All subscriptions attached to the topic.</param>
public sealed record SnsFanoutMap(
    string TopicArn,
    IReadOnlyList<SnsSubscriptionStatus> Subscriptions);
