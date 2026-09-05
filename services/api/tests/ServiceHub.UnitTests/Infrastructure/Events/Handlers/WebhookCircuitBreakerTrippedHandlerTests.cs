using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Events.Handlers;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Events.Handlers;

public sealed class WebhookCircuitBreakerTrippedHandlerTests
{
    private const long TestRuleId = 42L;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlatformEvent BuildTrippedEvent(int sampleSize = 20, double verifiedSuccessRate = 0.35) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Rule,
            EventType = EventTypes.AutoReplayRuleCircuitBreakerTripped,
            Severity = EventSeverity.Warning,
            Actor = "owner-a",
            TargetScope = TestRuleId.ToString(),
            Payload = new AutoReplayRuleCircuitBreakerTrippedPayload
            {
                RuleId = TestRuleId,
                RuleName = "orders-dlq-autoreplay",
                SampleSize = sampleSize,
                VerifiedSuccessRate = verifiedSuccessRate,
                AppliedSuccessRateFloor = 0.50,
                TrippedAtUtc = DateTimeOffset.UtcNow,
            },
        };

    private static PlatformEvent BuildUnrelatedEvent() =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Namespace,
            EventType = EventTypes.NamespaceCreated,
            Severity = EventSeverity.Info,
            Payload = new NamespaceCreatedPayload
            {
                NamespaceId = Guid.NewGuid(),
                NamespaceName = "other-ns",
                CloudProvider = "azure",
                AuthType = "ConnectionString",
                OwnerId = "__spa__",
            },
        };

    private static (WebhookCircuitBreakerTrippedHandler Handler, Mock<IWebhookNotifier> NotifierMock)
        BuildSut(bool notifierSucceeds = true)
    {
        var notifierMock = new Mock<IWebhookNotifier>();
        notifierMock
            .Setup(n => n.NotifyCircuitBreakerTrippedAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifierSucceeds
                ? Result.Success()
                : Result.Failure(Error.ExternalService("HOOK_ERR", "Webhook failed")));

        var services = new ServiceCollection();
        services.AddSingleton(notifierMock.Object);
        var sp = services.BuildServiceProvider();

        var handler = new WebhookCircuitBreakerTrippedHandler(
            sp,
            NullLogger<WebhookCircuitBreakerTrippedHandler>.Instance);

        return (handler, notifierMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new WebhookCircuitBreakerTrippedHandler(
            null!,
            NullLogger<WebhookCircuitBreakerTrippedHandler>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookCircuitBreakerTrippedHandler(
            Mock.Of<IServiceProvider>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── HandleAsync — correct event ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CircuitBreakerTrippedEvent_InvokesWebhookNotifierExactlyOnce()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildTrippedEvent(sampleSize: 20, verifiedSuccessRate: 0.35);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyCircuitBreakerTrippedAsync(
                TestRuleId,
                "orders-dlq-autoreplay",
                20,
                0.35,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── HandleAsync — filter ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_UnrelatedEventType_DoesNotInvokeWebhookNotifier()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildUnrelatedEvent();

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyCircuitBreakerTrippedAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── HandleAsync — null guard ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NullEvent_Throws()
    {
        var (handler, _) = BuildSut();

        var act = async () => await handler.HandleAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Subscriber failure isolation (bus contract) ──────────────────────────

    [Fact]
    public async Task HandleAsync_NotifierReturnsFailure_DoesNotThrow()
    {
        var (handler, _) = BuildSut(notifierSucceeds: false);
        var evt = BuildTrippedEvent();

        var act = async () => await handler.HandleAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
