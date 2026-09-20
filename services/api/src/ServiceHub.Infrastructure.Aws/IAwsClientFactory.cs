using Amazon.DynamoDBv2;
using Amazon.SQS;
using Amazon.SimpleNotificationService;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Factory that creates AWS SDK clients from a <see cref="Namespace"/> credential configuration.
/// Supports <c>AwsAccessKey</c> and <c>AwsIamRole</c> authentication modes.
/// </summary>
public interface IAwsClientFactory
{
    /// <summary>
    /// Creates or returns a cached <see cref="IAmazonSQS"/> client for the given namespace.
    /// </summary>
    /// <param name="ns">The namespace whose credentials and region to use.</param>
    /// <returns>An <see cref="IAmazonSQS"/> client ready for use.</returns>
    IAmazonSQS GetSqsClient(Namespace ns);

    /// <summary>
    /// Creates or returns a cached <see cref="IAmazonSimpleNotificationService"/> client.
    /// </summary>
    /// <param name="ns">The namespace whose credentials and region to use.</param>
    /// <returns>An <see cref="IAmazonSimpleNotificationService"/> client ready for use.</returns>
    IAmazonSimpleNotificationService GetSnsClient(Namespace ns);

    /// <summary>
    /// Creates or returns a cached <see cref="IAmazonDynamoDB"/> client — used only by the DLQ
    /// observer attestation reader (ADR-004; ADR-0011) to read the observer's own log table. Same
    /// namespace credentials as <see cref="GetSqsClient"/>; the observer's Lambda writes with its
    /// own separate IAM role, this client only ever reads.
    /// </summary>
    /// <param name="ns">The namespace whose credentials and region to use.</param>
    /// <returns>An <see cref="IAmazonDynamoDB"/> client ready for use.</returns>
    IAmazonDynamoDB GetDynamoDbClient(Namespace ns);

    /// <summary>
    /// Disposes and removes any cached SQS/SNS clients for the given namespace, e.g. when
    /// the namespace is deleted. A no-op if nothing is cached for that namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    void RemoveClient(Guid namespaceId);
}
