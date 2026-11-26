using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf.WellKnownTypes;
using ServiceHub.Core.Models;
using ServiceHub.Providers.Gcp;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>Tests for the subscription settings <see cref="GcpMessagingProvider"/> reads for the Configuration tab.</summary>
public sealed class GcpSubscriptionSettingsTests
{
    [Fact]
    public void BuildSubscriptionSettings_ReadsDeadlineRetentionAttemptsAndOrdering()
    {
        var sub = new Subscription
        {
            AckDeadlineSeconds = 20,
            MessageRetentionDuration = Duration.FromTimeSpan(TimeSpan.FromDays(7)),
            DeadLetterPolicy = new DeadLetterPolicy { MaxDeliveryAttempts = 5 },
            EnableMessageOrdering = true,
        };

        GcpMessagingProvider.BuildSubscriptionSettings(sub).Should().Equal(
            new EntitySetting("Acknowledgement deadline", "20 seconds"),
            new EntitySetting("Message retention", "7 days"),
            new EntitySetting("Maximum delivery attempts", "5"),
            new EntitySetting("Message ordering", "On"));
    }

    [Fact]
    public void BuildSubscriptionSettings_WithoutADeadLetterPolicy_DoesNotInventAMaximum()
    {
        var names = GcpMessagingProvider.BuildSubscriptionSettings(new Subscription { AckDeadlineSeconds = 10 }).Select(s => s.Label);

        names.Should().NotContain("Maximum delivery attempts");
    }
}
