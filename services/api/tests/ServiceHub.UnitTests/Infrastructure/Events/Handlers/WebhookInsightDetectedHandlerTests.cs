using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Events.Handlers;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Events.Handlers;

public sealed class WebhookInsightDetectedHandlerTests
{
    private static readonly Guid TestFindingId = Guid.NewGuid();
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlatformEvent BuildInsightEvent(InsightKind kind = InsightKind.Anomaly, int severity = 85) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Insight,
            EventType = EventTypes.InsightDetected,
            Severity = EventSeverity.Warning,
            Actor = "owner-a",
            NamespaceId = TestNamespaceId,
            NamespaceName = "orders-ns",
            Payload = new InsightDetectedPayload
            {
                Kind = kind,
                FindingId = TestFindingId,
                EntityName = "orders-queue",
                Description = "spike detected",
                Severity = severity,
                DetectedAtUtc = DateTimeOffset.UtcNow,
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

    private static (WebhookInsightDetectedHandler Handler, Mock<IWebhookNotifier> NotifierMock)
        BuildSut(bool notifierSucceeds = true)
    {
        var notifierMock = new Mock<IWebhookNotifier>();
        notifierMock
            .Setup(n => n.NotifyInsightDetectedAsync(
                It.IsAny<InsightKind>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifierSucceeds
                ? Result.Success()
                : Result.Failure(Error.ExternalService("HOOK_ERR", "Webhook failed")));

        var services = new ServiceCollection();
        services.AddSingleton(notifierMock.Object);
        var sp = services.BuildServiceProvider();

        var handler = new WebhookInsightDetectedHandler(sp, NullLogger<WebhookInsightDetectedHandler>.Instance);

        return (handler, notifierMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new WebhookInsightDetectedHandler(null!, NullLogger<WebhookInsightDetectedHandler>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookInsightDetectedHandler(Mock.Of<IServiceProvider>(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── HandleAsync — correct event ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_InsightDetectedEvent_InvokesWebhookNotifierExactlyOnce()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildInsightEvent(InsightKind.Drift, severity: 90);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyInsightDetectedAsync(
                InsightKind.Drift,
                TestFindingId,
                TestNamespaceId,
                "orders-ns",
                "orders-queue",
                "spike detected",
                90,
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
            n => n.NotifyInsightDetectedAsync(
                It.IsAny<InsightKind>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
        var evt = BuildInsightEvent();

        var act = async () => await handler.HandleAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
