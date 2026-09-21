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

public sealed class WebhookBulkOperationCompletedHandlerTests
{
    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlatformEvent BuildCompletedEvent(
        BulkOperationType operationType = BulkOperationType.Replay,
        BulkOperationStatus status = BulkOperationStatus.Completed,
        int successCount = 3,
        int failureCount = 0,
        int skippedCount = 0) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.BulkOperation,
            EventType = EventTypes.BulkOperationCompleted,
            Severity = EventSeverity.Info,
            NamespaceId = TestNamespaceId,
            NamespaceName = "test-ns",
            Payload = new BulkOperationCompletedPayload
            {
                JobId = TestJobId,
                OperationType = operationType,
                Status = status,
                NamespaceId = TestNamespaceId,
                NamespaceName = "test-ns",
                TotalMatched = successCount + failureCount + skippedCount,
                SuccessCount = successCount,
                FailureCount = failureCount,
                SkippedCount = skippedCount,
                CompletedAtUtc = DateTimeOffset.UtcNow,
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

    private static (WebhookBulkOperationCompletedHandler Handler, Mock<IWebhookNotifier> NotifierMock)
        BuildSut(bool notifierSucceeds = true)
    {
        var notifierMock = new Mock<IWebhookNotifier>();
        notifierMock
            .Setup(n => n.NotifyBulkOperationCompletedAsync(
                It.IsAny<Guid>(),
                It.IsAny<BulkOperationType>(),
                It.IsAny<BulkOperationStatus>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifierSucceeds
                ? Result.Success()
                : Result.Failure(Error.ExternalService("HOOK_ERR", "Webhook failed")));

        var services = new ServiceCollection();
        services.AddSingleton(notifierMock.Object);
        var sp = services.BuildServiceProvider();

        var handler = new WebhookBulkOperationCompletedHandler(
            sp,
            NullLogger<WebhookBulkOperationCompletedHandler>.Instance);

        return (handler, notifierMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new WebhookBulkOperationCompletedHandler(
            null!,
            NullLogger<WebhookBulkOperationCompletedHandler>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookBulkOperationCompletedHandler(
            Mock.Of<IServiceProvider>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── HandleAsync — correct event ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_BulkOperationCompletedEvent_InvokesWebhookNotifierExactlyOnce()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildCompletedEvent(successCount: 5, failureCount: 1);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyBulkOperationCompletedAsync(
                TestJobId,
                BulkOperationType.Replay,
                BulkOperationStatus.Completed,
                TestNamespaceId,
                "test-ns",
                6,
                5,
                1,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PurgeJob_PassesCorrectOperationType()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildCompletedEvent(operationType: BulkOperationType.Purge, status: BulkOperationStatus.CompletedWithErrors);

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyBulkOperationCompletedAsync(
                It.IsAny<Guid>(),
                BulkOperationType.Purge,
                BulkOperationStatus.CompletedWithErrors,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
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
            n => n.NotifyBulkOperationCompletedAsync(
                It.IsAny<Guid>(),
                It.IsAny<BulkOperationType>(),
                It.IsAny<BulkOperationStatus>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
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
        var (handler, _) = BuildSut(notifierSucceeds: false);
        var evt = BuildCompletedEvent();

        var act = async () => await handler.HandleAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
