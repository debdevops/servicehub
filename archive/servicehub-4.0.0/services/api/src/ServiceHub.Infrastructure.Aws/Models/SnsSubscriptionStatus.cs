namespace ServiceHub.Infrastructure.Aws.Models;

/// <summary>
/// Represents an individual SNS subscription on a topic.
/// </summary>
/// <param name="SubscriptionArn">The subscription ARN (or "PendingConfirmation" if not yet confirmed).</param>
/// <param name="Protocol">The delivery protocol: <c>email</c>, <c>sqs</c>, <c>lambda</c>, <c>https</c>, etc.</param>
/// <param name="Endpoint">The delivery endpoint (email address, SQS ARN, Lambda ARN, or URL).</param>
/// <param name="Status">
/// The confirmation status: <c>Confirmed</c>, <c>PendingConfirmation</c>, or <c>Deleted</c>.
/// Unconfirmed subscriptions silently drop messages — a common fanout debugging issue.
/// </param>
public sealed record SnsSubscriptionStatus(
    string SubscriptionArn,
    string Protocol,
    string Endpoint,
    string Status);
