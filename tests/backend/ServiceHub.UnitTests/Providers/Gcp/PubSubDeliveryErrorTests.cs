using FluentAssertions;
using ServiceHub.Providers.Gcp;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>
/// G8. The attribute sets are what a real project returned on 2026-11-11 for a dead letter from each kind of subscription, with the
/// project name replaced. Only the export subscription's dead letter says why.
/// </summary>
public sealed class PubSubDeliveryErrorTests
{
    private static Dictionary<string, string> Stamp(string subscription) => new()
    {
        ["CloudPubSubDeadLetterSourceDeliveryCount"] = "5",
        ["CloudPubSubDeadLetterSourceSubscription"] = subscription,
        ["CloudPubSubDeadLetterSourceSubscriptionProject"] = "my-project",
        ["CloudPubSubDeadLetterSourceTopicPublishTime"] = "2026-11-11T06:28:23.956+00:00",
    };

    [Fact]
    public void ExportSubscriptionDeadLetter_CarriesTheErrorText()
    {
        var attributes = Stamp("servicehub-g8-tmp-export");
        attributes["CloudPubSubDeadLetterSourceDeliveryErrorMessage"] = "syntax error while parsing value - invalid literal; last read: 'th'";
        attributes["tenant"] = "acme";

        GcpMessageReceiver.DeliveryError(attributes).Should().Be("syntax error while parsing value - invalid literal; last read: 'th'");
    }

    [Theory]
    [InlineData("servicehub-dev-orders-subscription")]                          // pull
    [InlineData("eventarc-asia-south1-servicehub-livetest-trigger-sub-475")]    // push, endpoint refuses
    public void PullAndPushDeadLetters_CarryNoError(string subscription)
    {
        GcpMessageReceiver.DeliveryError(Stamp(subscription)).Should().BeNull();
    }

    [Fact]
    public void ABlankError_IsNoError()
    {
        var attributes = Stamp("s");
        attributes[GcpMessageReceiver.DeliveryErrorAttribute] = "  ";

        GcpMessageReceiver.DeliveryError(attributes).Should().BeNull();
    }

    [Fact]
    public void ALongError_IsCut()
    {
        var attributes = Stamp("s");
        attributes[GcpMessageReceiver.DeliveryErrorAttribute] = new string('x', 4000);

        GcpMessageReceiver.DeliveryError(attributes).Should().HaveLength(GcpMessageReceiver.MaxDeliveryErrorLength);
    }
}
