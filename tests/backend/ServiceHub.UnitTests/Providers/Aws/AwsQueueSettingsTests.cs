using FluentAssertions;
using ServiceHub.Core.Models;
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
}
