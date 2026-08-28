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

public sealed class WebhookAutonomyTransitionHandlerTests
{
    private const string TestOwnerId = "owner-a";
    private const string TestSignatureHash = "sig-abc123";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlatformEvent BuildTransitionEvent(
        AutonomyLevel previousLevel = AutonomyLevel.Approve,
        AutonomyLevel newLevel = AutonomyLevel.Standing,
        string reason = "Promoted Approve→Standing: n=10, verified_success_rate=100%") =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Autonomy,
            EventType = EventTypes.AutonomyGrantTransitioned,
            Severity = EventSeverity.Info,
            Actor = TestOwnerId,
            TargetScope = TestSignatureHash,
            Payload = new AutonomyGrantTransitionedPayload
            {
                OwnerId = TestOwnerId,
                SignatureHash = TestSignatureHash,
                OperationKind = RecoveryOperationKind.Replay,
                PreviousLevel = previousLevel,
                NewLevel = newLevel,
                Reason = reason,
                TransitionedAtUtc = DateTimeOffset.UtcNow,
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

    private static (WebhookAutonomyTransitionHandler Handler, Mock<IWebhookNotifier> NotifierMock)
        BuildSut(bool notifierSucceeds = true)
    {
        var notifierMock = new Mock<IWebhookNotifier>();
        notifierMock
            .Setup(n => n.NotifyAutonomyTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<AutonomyLevel>(),
                It.IsAny<AutonomyLevel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifierSucceeds
                ? Result.Success()
                : Result.Failure(Error.ExternalService("HOOK_ERR", "Webhook failed")));

        var services = new ServiceCollection();
        services.AddSingleton(notifierMock.Object);
        var sp = services.BuildServiceProvider();

        var handler = new WebhookAutonomyTransitionHandler(
            sp,
            NullLogger<WebhookAutonomyTransitionHandler>.Instance);

        return (handler, notifierMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new WebhookAutonomyTransitionHandler(
            null!,
            NullLogger<WebhookAutonomyTransitionHandler>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookAutonomyTransitionHandler(
            Mock.Of<IServiceProvider>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── HandleAsync — correct event ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AutonomyTransitionEvent_InvokesWebhookNotifierExactlyOnce()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildTransitionEvent();

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyAutonomyTransitionAsync(
                TestSignatureHash,
                AutonomyLevel.Approve,
                AutonomyLevel.Standing,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DemotionEvent_PassesCorrectLevels()
    {
        var (handler, notifierMock) = BuildSut();
        var evt = BuildTransitionEvent(previousLevel: AutonomyLevel.Standing, newLevel: AutonomyLevel.Approve, reason: "Demoted");

        await handler.HandleAsync(evt, CancellationToken.None);

        notifierMock.Verify(
            n => n.NotifyAutonomyTransitionAsync(
                It.IsAny<string>(),
                AutonomyLevel.Standing,
                AutonomyLevel.Approve,
                "Demoted",
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
            n => n.NotifyAutonomyTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<AutonomyLevel>(),
                It.IsAny<AutonomyLevel>(),
                It.IsAny<string>(),
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
        var evt = BuildTransitionEvent();

        var act = async () => await handler.HandleAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
