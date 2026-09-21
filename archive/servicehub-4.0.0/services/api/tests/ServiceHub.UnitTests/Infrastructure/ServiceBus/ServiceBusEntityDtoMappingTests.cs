using System.Reflection;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FluentAssertions;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Infrastructure.ServiceBus;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

/// <summary>
/// The list endpoints (<c>GET /namespaces/{id}/queues|topics|subscriptions</c>) are served by
/// <c>GetQueuesRuntimePropertiesAsync</c> and friends, which return message counts only — never
/// the entity's static configuration. The DTO still carries fields for that configuration, so the
/// mapper has to fill them with something.
///
/// It used to fill them with the Service Bus SDK's own defaults (max delivery count 10, lock
/// duration 1 minute, TTL <see cref="TimeSpan.MaxValue"/>). Those are plausible values, which is
/// exactly what makes them dangerous: a caller cannot tell them apart from values genuinely read
/// off the broker. Verified live against a real namespace on 2026-09-05 — the list endpoint
/// reported <c>lockDuration 00:01:00</c> and <c>defaultMessageTimeToLive</c> ≈ infinity for a
/// queue whose real values were 30 seconds and 14 days.
///
/// These tests pin the mapper to neutral "not fetched" values on the list path, and to real
/// values on the single-entity path (which does read the static configuration).
/// </summary>
public sealed class ServiceBusEntityDtoMappingTests
{
    private static T Invoke<T>(string method, params object?[] args)
    {
        var mi = typeof(ServiceBusClientWrapper)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{method} not found — was it renamed?");
        return (T)mi.Invoke(null, args)!;
    }

    // ── Queues ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MapToQueueDto_WithoutStaticProperties_ReportsNeutralValuesNotSdkDefaults()
    {
        var runtime = ServiceBusModelFactory.QueueRuntimeProperties(
            name: "orders",
            activeMessageCount: 0,
            deadLetterMessageCount: 376,
            sizeInBytes: 372012);

        var dto = Invoke<QueueRuntimePropertiesDto>("MapToQueueDto", runtime, null);

        // Counts are genuinely fetched — they must survive untouched.
        dto.Name.Should().Be("orders");
        dto.DeadLetterMessageCount.Should().Be(376);
        dto.SizeInBytes.Should().Be(372012);

        // Static configuration was NOT fetched — nothing may look like a real broker value.
        dto.Status.Should().Be("Unknown");
        dto.MaxDeliveryCount.Should().Be(0, "10 is the SDK default and is indistinguishable from a real value");
        dto.LockDuration.Should().Be(TimeSpan.Zero, "1 minute is the SDK default and reads as a real lock duration");
        dto.DefaultMessageTimeToLive.Should().Be(TimeSpan.Zero, "TimeSpan.MaxValue reads as 'never expires'");
        dto.AutoDeleteOnIdle.Should().Be(TimeSpan.Zero);
        dto.MaxSizeInMegabytes.Should().Be(0);
        dto.EnableBatchedOperations.Should().BeFalse("true is the SDK default, not an observed fact");
    }

    [Fact]
    public void MapToQueueDto_WithStaticProperties_ReportsTheRealBrokerValues()
    {
        var runtime = ServiceBusModelFactory.QueueRuntimeProperties(
            name: "orders",
            activeMessageCount: 0,
            deadLetterMessageCount: 376);

        // QueueProperties' constructor is internal to the SDK — the only supported way to obtain
        // one is from a live admin call, so construct it directly for this unit test.
        var props = (QueueProperties)Activator.CreateInstance(
            typeof(QueueProperties),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: new object[] { "orders" },
            culture: null)!;
        props.MaxDeliveryCount = 5;
        props.LockDuration = TimeSpan.FromSeconds(30);
        props.DefaultMessageTimeToLive = TimeSpan.FromDays(14);
        props.MaxSizeInMegabytes = 5120;
        props.EnableBatchedOperations = true;

        var dto = Invoke<QueueRuntimePropertiesDto>("MapToQueueDto", runtime, props);

        dto.Status.Should().Be(props.Status.ToString());
        dto.MaxDeliveryCount.Should().Be(5);
        dto.LockDuration.Should().Be(TimeSpan.FromSeconds(30));
        dto.DefaultMessageTimeToLive.Should().Be(TimeSpan.FromDays(14));
        dto.MaxSizeInMegabytes.Should().Be(5120);
        dto.EnableBatchedOperations.Should().BeTrue();
    }

    // ── Topics ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MapToTopicDto_WithoutStaticProperties_ReportsNeutralValuesNotSdkDefaults()
    {
        var runtime = ServiceBusModelFactory.TopicRuntimeProperties(
            name: "orders-topic",
            subscriptionCount: 2,
            sizeInBytes: 1024);

        var dto = Invoke<TopicRuntimePropertiesDto>("MapToTopicDto", runtime, null);

        dto.Name.Should().Be("orders-topic");
        dto.SubscriptionCount.Should().Be(2);

        dto.Status.Should().Be("Unknown");
        dto.DefaultMessageTimeToLive.Should().Be(TimeSpan.Zero);
        dto.AutoDeleteOnIdle.Should().Be(TimeSpan.Zero);
        dto.DuplicateDetectionHistoryTimeWindow.Should().Be(TimeSpan.Zero, "10 minutes is the SDK default");
        dto.MaxSizeInMegabytes.Should().Be(0);
        dto.EnableBatchedOperations.Should().BeFalse();
    }

    // ── Subscriptions ────────────────────────────────────────────────────────────

    [Fact]
    public void MapToSubscriptionDto_WithoutStaticProperties_ReportsNeutralValuesNotSdkDefaults()
    {
        var runtime = ServiceBusModelFactory.SubscriptionRuntimeProperties(
            topicName: "orders-topic",
            subscriptionName: "all-orders",
            activeMessageCount: 3,
            deadLetterMessageCount: 7);

        var dto = Invoke<SubscriptionRuntimePropertiesDto>("MapToSubscriptionDto", runtime, null);

        dto.Name.Should().Be("all-orders");
        dto.TopicName.Should().Be("orders-topic");
        dto.DeadLetterMessageCount.Should().Be(7);

        dto.Status.Should().Be("Unknown");
        dto.MaxDeliveryCount.Should().Be(0);
        dto.LockDuration.Should().Be(TimeSpan.Zero);
        dto.DefaultMessageTimeToLive.Should().Be(TimeSpan.Zero);
        dto.AutoDeleteOnIdle.Should().Be(TimeSpan.Zero);
        dto.EnableBatchedOperations.Should().BeFalse();
    }
}
