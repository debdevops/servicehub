using FluentAssertions;
using Google.Cloud.Eventarc.V1;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Gcp;
using ServiceHub.Providers.Gcp.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>
/// The parts of Google's side of the Eventarc source that can be tried without Google's paged list types: reading one trigger and one
/// subscription's dead-letter policy. The paged lists are proven live (the same stand-in is used for the Workflows reader).
/// </summary>
public sealed class GcpEventarcReaderTests
{
    private readonly Mock<EventarcClient> _eventarc = new();
    private readonly Mock<SubscriberServiceApiClient> _subscriber = new();
    private readonly GcpEventarcReader _reader;
    private readonly Namespace _ns = Namespace.Create("my-proj", "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: "my-proj").Value;

    public GcpEventarcReaderTests()
    {
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetEventarcClientAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>())).ReturnsAsync(_eventarc.Object);
        factory.Setup(f => f.GetSubscriberClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(_subscriber.Object);
        _reader = new GcpEventarcReader(factory.Object);
    }

    [Fact]
    public async Task A_trigger_gives_its_name_and_the_pubsub_subscription_eventarc_delivers_through()
    {
        _eventarc.Setup(c => c.GetTriggerAsync(It.IsAny<GetTriggerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trigger { Name = "projects/my-proj/locations/us-central1/triggers/orders", Transport = new Transport { Pubsub = new Pubsub { Subscription = "projects/my-proj/subscriptions/eventarc-orders" } } });

        var trigger = await _reader.GetTriggerAsync(_ns, "projects/my-proj/locations/us-central1/triggers/orders", default);

        trigger.Should().Be(new TriggerRef("projects/my-proj/locations/us-central1/triggers/orders", "projects/my-proj/subscriptions/eventarc-orders"));
        _eventarc.Verify(c => c.GetTriggerAsync(It.Is<GetTriggerRequest>(r => r.Name == "projects/my-proj/locations/us-central1/triggers/orders"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_trigger_with_no_transport_subscription_has_none()
    {
        _eventarc.Setup(c => c.GetTriggerAsync(It.IsAny<GetTriggerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trigger { Name = "projects/my-proj/locations/us-central1/triggers/orders" });

        (await _reader.GetTriggerAsync(_ns, "x", default))!.TransportSubscription.Should().BeNull();
    }

    [Fact]
    public async Task A_trigger_google_does_not_have_is_null_and_any_other_failure_is_not_swallowed()
    {
        _eventarc.SetupSequence(c => c.GetTriggerAsync(It.IsAny<GetTriggerRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "gone")))
            .ThrowsAsync(new RpcException(new Status(StatusCode.PermissionDenied, "no")));

        (await _reader.GetTriggerAsync(_ns, "x", default)).Should().BeNull();
        await ((Func<Task>)(() => _reader.GetTriggerAsync(_ns, "x", default))).Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task A_subscription_with_a_dead_letter_policy_gives_its_topic()
    {
        _subscriber.Setup(c => c.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription { Name = "s", DeadLetterPolicy = new DeadLetterPolicy { DeadLetterTopic = "projects/my-proj/topics/orders-dlt" } });

        (await _reader.GetDeadLetterTopicAsync(_ns, "projects/my-proj/subscriptions/eventarc-orders", default)).Should().Be("projects/my-proj/topics/orders-dlt");
        _subscriber.Verify(c => c.GetSubscriptionAsync(It.Is<GetSubscriptionRequest>(r => r.Subscription == "projects/my-proj/subscriptions/eventarc-orders"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_subscription_with_no_policy_or_an_empty_one_keeps_nothing(bool emptyPolicy)
    {
        _subscriber.Setup(c => c.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription { Name = "s", DeadLetterPolicy = emptyPolicy ? new DeadLetterPolicy() : null });

        (await _reader.GetDeadLetterTopicAsync(_ns, "s", default)).Should().BeNull();
    }

    [Fact]
    public async Task A_subscription_that_is_gone_keeps_nothing_and_another_failure_is_not_swallowed()
    {
        _subscriber.SetupSequence(c => c.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "gone")))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));

        (await _reader.GetDeadLetterTopicAsync(_ns, "s", default)).Should().BeNull();
        await ((Func<Task>)(() => _reader.GetDeadLetterTopicAsync(_ns, "s", default))).Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task The_reader_only_ever_asks_for_one_and_reads_nothing_it_could_change()
    {
        _eventarc.Setup(c => c.GetTriggerAsync(It.IsAny<GetTriggerRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Trigger { Name = "t" });
        _subscriber.Setup(c => c.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Subscription { Name = "s" });

        await _reader.GetTriggerAsync(_ns, "t", default);
        await _reader.GetDeadLetterTopicAsync(_ns, "s", default);

        _eventarc.Invocations.Select(i => i.Method.Name).Should().OnlyContain(n => n.StartsWith("Get", StringComparison.Ordinal));
        _subscriber.Invocations.Select(i => i.Method.Name).Should().OnlyContain(n => n.StartsWith("Get", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_client_factory_is_refused()
    {
        ((Action)(() => _ = new GcpEventarcReader(null!))).Should().Throw<ArgumentNullException>();
    }
}
