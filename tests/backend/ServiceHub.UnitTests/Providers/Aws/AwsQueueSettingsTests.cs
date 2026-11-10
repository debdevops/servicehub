using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Providers.Aws;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>Tests for the queue settings <see cref="AwsMessagingProvider"/> reads for the Configuration tab.</summary>
public sealed class AwsQueueSettingsTests
{
    [Fact]
    public void BuildQueueSettings_NamesEachLimitAndReadsTheRedrivePolicy()
    {
        var settings = AwsMessagingProvider.BuildQueueSettings(new Dictionary<string, string>
        {
            ["VisibilityTimeout"] = "30",
            ["MessageRetentionPeriod"] = "345600",
            ["DelaySeconds"] = "0",
            ["FifoQueue"] = "false",
            ["RedrivePolicy"] = "{\"deadLetterTargetArn\":\"arn:aws:sqs:us-east-1:1:orders-dlq\",\"maxReceiveCount\":5}",
        });

        settings.Should().Equal(
            new EntitySetting("Visibility timeout", "30 seconds"),
            new EntitySetting("Message retention", "4 days"),
            new EntitySetting("Delivery delay", "0 seconds"),
            new EntitySetting("First-in first-out", "No"),
            new EntitySetting("Maximum receives before dead-letter", "5"));
    }

    [Fact]
    public void BuildQueueSettings_WithoutARedrivePolicy_DoesNotInventAMaximum()
    {
        var settings = AwsMessagingProvider.BuildQueueSettings(new Dictionary<string, string> { ["VisibilityTimeout"] = "30" });

        settings.Should().ContainSingle().Which.Label.Should().Be("Visibility timeout");
    }

    [Fact]
    public void BuildQueueSettings_WithNoAttributes_IsEmptyNotAGuess() =>
        AwsMessagingProvider.BuildQueueSettings(null).Should().BeEmpty();

    [Fact]
    public async Task GetEntitySettings_OnAStandardQueue_ReadsTheLimits_AndSaysItIsNotFifo()
    {
        // Live 2026-11-10: real SQS refuses to be asked for FifoQueue by name on a standard queue, so "Show limits" answered 502 for
        // every standard queue. This fake refuses the same way.
        var ns = Namespace.Create(
            "test-aws-ns", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(c => c.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "https://sqs.us-east-1.amazonaws.com/1/orders" });
        sqs.Setup(c => c.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetQueueAttributesRequest request, CancellationToken _) =>
            {
                if (request.AttributeNames.Contains("FifoQueue"))
                {
                    throw new AmazonSQSException("Unknown Attribute FifoQueue.") { ErrorCode = "InvalidAttributeName" };
                }

                return new GetQueueAttributesResponse { Attributes = new Dictionary<string, string> { ["VisibilityTimeout"] = "30" } };
            });
        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqs.Object);
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));
        var provider = new AwsMessagingProvider(
            factory.Object,
            new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object, new Mock<INamespaceRepository>().Object, NullLogger<AwsMessageReceiver>.Instance),
            new AwsMessageSender(new Mock<IAwsClientFactory>().Object, new Mock<INamespaceRepository>().Object, NullLogger<AwsMessageSender>.Instance),
            repo.Object, NullLogger<AwsMessagingProvider>.Instance);

        var result = await provider.GetEntitySettingsAsync(ns.Id, "orders", "queue", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(
            new EntitySetting("Visibility timeout", "30 seconds"),
            new EntitySetting("First-in first-out", "No"));
    }
}
