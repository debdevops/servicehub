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

public sealed class WebhookDlqSpikeHandlerTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlatformEvent BuildSpikeEvent(int messageCount = 5) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Dlq,
            EventType = EventTypes.DlqSpikeDetected,
            Severity = EventSeverity.Warning,
            NamespaceId = TestNamespaceId,
            NamespaceName = "test-ns",
            Payload = new DlqSpikeDetectedPayload
            {
                NamespaceId = TestNamespaceId,
                NamespaceName = "test-ns",
                NewMessageCount = messageCount,
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

    private static (WebhookDlqSpikeHandler Handler, Mock<IWebhookNotifier> NotifierMock)
        BuildSut(bool notifierSucceeds = true)
    {
        var notifierMock = new Mock<IWebhookNotifier>();
        notifierMock
            .Setup(n => n.NotifyDlqSpikeAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifierSucceeds
                ? Result.Success()
                : Result.Failure(Error.ExternalService("HOOK_ERR", "Webhook failed")));

        var services = new ServiceCollection();
        services.AddSingleton(notifierMock.Object);
        var sp = services.BuildServiceProvider();

        var handler = new WebhookDlqSpikeHandler(
            sp,
            NullLogger<WebhookDlqSpikeHandler>.Instance);

        return (handler, notifierMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new WebhookDlqSpikeHandler(
            null!,
            NullLogger<WebhookDlqSpikeHandler>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookDlqSpikeHandler(
            Mock.Of<IServiceProvider>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── HandleAsync — correct event ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DlqSpikeEvent_InvokesWebhookNotifierExactlyOnce()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildSpikeEvent(messageCount: 12);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyDlqSpikeAsync(
                TestNamespaceId,
                "test-ns",
                12,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DlqSpikeEvent_PassesCorrectMessageCount()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildSpikeEvent(messageCount: 99);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyDlqSpikeAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                99,
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
            n => n.NotifyDlqSpikeAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
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
        // IWebhookNotifier returning Result.Failure should not propagate as an exception.
        var (handler, _) = BuildSut(notifierSucceeds: false);
        var evt = BuildSpikeEvent();

        var act = async () => await handler.HandleAsync(evt, CancellationToken.None);

        // NotifyDlqSpikeAsync returns a Result — handler must not throw on failure result.
        await act.Should().NotThrowAsync();
    }
}
