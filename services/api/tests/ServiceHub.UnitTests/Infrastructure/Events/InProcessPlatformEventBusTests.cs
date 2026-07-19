using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Events;
using ServiceHub.Infrastructure.Events;

namespace ServiceHub.UnitTests.Infrastructure.Events;

/// <summary>
/// Regression pack for the in-process event bus — the delivery backbone for
/// DLQ-spike webhooks and SSE. Pins the reliability contract: publishing never
/// blocks, subscribers receive events in order, and one failing subscriber
/// can neither crash the bus nor starve the others.
/// </summary>
public sealed class InProcessPlatformEventBusTests
{
    private static PlatformEvent BuildEvent(string type = "test.event") => new()
    {
        Source = "unit-test",
        Category = EventCategories.Dlq,
        EventType = type,
        Severity = EventSeverity.Info,
    };

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new InProcessPlatformEventBus(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task PublishAsync_NullEvent_Throws()
    {
        using var bus = new InProcessPlatformEventBus(NullLogger<InProcessPlatformEventBus>.Instance);
        var act = async () => await bus.PublishAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Subscribe_NullHandler_Throws()
    {
        using var bus = new InProcessPlatformEventBus(NullLogger<InProcessPlatformEventBus>.Instance);
        var act = () => bus.Subscribe(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishedEvents_AreDeliveredToAllSubscribersInOrder()
    {
        using var bus = new InProcessPlatformEventBus(NullLogger<InProcessPlatformEventBus>.Instance);
        var firstSeen = new List<string>();
        var secondSeen = new List<string>();
        bus.Subscribe((e, _) => { firstSeen.Add(e.EventType); return Task.CompletedTask; });
        bus.Subscribe((e, _) => { secondSeen.Add(e.EventType); return Task.CompletedTask; });

        await bus.StartAsync(CancellationToken.None);
        await bus.PublishAsync(BuildEvent("evt-1"));
        await bus.PublishAsync(BuildEvent("evt-2"));

        // Drain loop is async — wait for delivery, then stop.
        await WaitUntilAsync(() => secondSeen.Count == 2);
        await bus.StopAsync(CancellationToken.None);

        firstSeen.Should().Equal("evt-1", "evt-2");
        secondSeen.Should().Equal("evt-1", "evt-2");
    }

    [Fact]
    public async Task FailingSubscriber_DoesNotStopDeliveryToOthers()
    {
        using var bus = new InProcessPlatformEventBus(NullLogger<InProcessPlatformEventBus>.Instance);
        var delivered = new List<string>();
        bus.Subscribe((_, _) => throw new InvalidOperationException("subscriber boom"));
        bus.Subscribe((e, _) => { delivered.Add(e.EventType); return Task.CompletedTask; });

        await bus.StartAsync(CancellationToken.None);
        await bus.PublishAsync(BuildEvent("evt-a"));
        await bus.PublishAsync(BuildEvent("evt-b"));

        await WaitUntilAsync(() => delivered.Count == 2);
        await bus.StopAsync(CancellationToken.None);

        // The healthy subscriber saw every event despite its neighbour throwing.
        delivered.Should().Equal("evt-a", "evt-b");
    }

    [Fact]
    public async Task PublishAsync_CompletesImmediatelyEvenWithNoDrainLoop()
    {
        // Publishers must never block: without the drain loop running, publishing
        // still returns instantly because the channel buffers (DropOldest on full).
        using var bus = new InProcessPlatformEventBus(NullLogger<InProcessPlatformEventBus>.Instance);

        var publish = bus.PublishAsync(BuildEvent()).AsTask();
        var finished = await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(2)));

        finished.Should().Be(publish);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        condition().Should().BeTrue("the expected deliveries should arrive within the timeout");
    }
}
