using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Services;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Services;

public sealed class PlatformEventStreamBrokerTests
{
    private const string OwnerA = "__spa__";
    private const string OwnerB = "key_abcdef123456";
    private const int MaxConnections = 20;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Namespace BuildNamespace(string ownerId) =>
        Namespace.Create(
            "test-ns.servicebus.windows.net",
            "Endpoint=sb://test-ns.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdGtleXZhbHVldGVzdGtleXZhbHVl",
            ownerId: ownerId).Value;

    private static PlatformEvent BuildDlqSpikeEvent(Guid? namespaceId = null, string? actor = null) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Dlq,
            EventType = EventTypes.DlqSpikeDetected,
            Severity = EventSeverity.Warning,
            NamespaceId = namespaceId,
            NamespaceName = "test-ns",
            Actor = actor,
        };

    private static PlatformEvent BuildNamespaceCreatedEvent(string actor) =>
        new()
        {
            Source = "Test",
            Category = EventCategories.Namespace,
            EventType = EventTypes.NamespaceCreated,
            Severity = EventSeverity.Info,
            Actor = actor,
        };

    private static (PlatformEventStreamBroker Broker, Mock<INamespaceRepository> RepositoryMock)
        BuildSut(params (string OwnerId, IReadOnlyList<Namespace> Namespaces)[] ownerNamespaces)
    {
        var repositoryMock = new Mock<INamespaceRepository>();
        repositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([]));

        foreach (var (ownerId, namespaces) in ownerNamespaces)
        {
            repositoryMock
                .Setup(r => r.GetByOwnerAsync(ownerId, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(namespaces));
        }

        var services = new ServiceCollection();
        services.AddSingleton(repositoryMock.Object);
        var sp = services.BuildServiceProvider();

        var broker = new PlatformEventStreamBroker(
            sp,
            NullLogger<PlatformEventStreamBroker>.Instance);

        return (broker, repositoryMock);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new PlatformEventStreamBroker(
            null!,
            NullLogger<PlatformEventStreamBroker>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new PlatformEventStreamBroker(
            Mock.Of<IServiceProvider>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public void Register_EmptyOwnerId_Throws()
    {
        var (broker, _) = BuildSut();

        var act = () => broker.Register(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_UnderCap_ReturnsSubscription()
    {
        var (broker, _) = BuildSut();

        using var subscription = broker.Register(OwnerA);

        subscription.Should().NotBeNull();
    }

    [Fact]
    public void Register_OverCap_ReturnsNull()
    {
        var (broker, _) = BuildSut();
        var subscriptions = Enumerable.Range(0, MaxConnections)
            .Select(_ => broker.Register(OwnerA))
            .ToList();
        subscriptions.Should().OnlyContain(s => s != null);

        var overCap = broker.Register(OwnerA);

        overCap.Should().BeNull();
        subscriptions.ForEach(s => s!.Dispose());
    }

    [Fact]
    public void Register_AfterDisposeFreesSlot_Succeeds()
    {
        var (broker, _) = BuildSut();
        var subscriptions = Enumerable.Range(0, MaxConnections)
            .Select(_ => broker.Register(OwnerA)!)
            .ToList();

        subscriptions[0].Dispose();
        using var subscription = broker.Register(OwnerA);

        subscription.Should().NotBeNull();
        subscriptions.Skip(1).ToList().ForEach(s => s.Dispose());
    }

    // ── HandleAsync — guards and filtering ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_NullEvent_Throws()
    {
        var (broker, _) = BuildSut();

        var act = async () => await broker.HandleAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_UnstreamedEventType_NotDelivered()
    {
        var (broker, _) = BuildSut();
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent(actor: OwnerA) with { EventType = "servicehub.test.unknown.v1" };

        await broker.HandleAsync(evt, CancellationToken.None);

        subscription.Reader.TryRead(out _).Should().BeFalse();
    }

    // ── HandleAsync — owner scoping ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ActorMatchesOwner_Delivered()
    {
        var (broker, _) = BuildSut();
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent(actor: OwnerA);

        await broker.HandleAsync(evt, CancellationToken.None);

        subscription.Reader.TryRead(out var delivered).Should().BeTrue();
        delivered!.EventType.Should().Be(EventTypes.DlqSpikeDetected);
    }

    [Fact]
    public async Task HandleAsync_NamespaceOwnedByOwner_Delivered()
    {
        var ns = BuildNamespace(OwnerA);
        var (broker, _) = BuildSut((OwnerA, [ns]));
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent(namespaceId: ns.Id);

        await broker.HandleAsync(evt, CancellationToken.None);

        subscription.Reader.TryRead(out var delivered).Should().BeTrue();
        delivered!.NamespaceId.Should().Be(ns.Id);
    }

    [Fact]
    public async Task HandleAsync_NamespaceNotOwned_NotDelivered()
    {
        var (broker, _) = BuildSut((OwnerA, []));
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent(namespaceId: Guid.NewGuid());

        await broker.HandleAsync(evt, CancellationToken.None);

        subscription.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NoActorAndNoNamespaceId_NotDelivered()
    {
        var (broker, _) = BuildSut();
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent();

        await broker.HandleAsync(evt, CancellationToken.None);

        subscription.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_TwoOwners_OnlyOwningConnectionReceivesEvent()
    {
        var ns = BuildNamespace(OwnerA);
        var (broker, _) = BuildSut((OwnerA, [ns]), (OwnerB, Array.Empty<Namespace>()));
        using var subscriptionA = broker.Register(OwnerA)!;
        using var subscriptionB = broker.Register(OwnerB)!;
        var evt = BuildDlqSpikeEvent(namespaceId: ns.Id);

        await broker.HandleAsync(evt, CancellationToken.None);

        subscriptionA.Reader.TryRead(out _).Should().BeTrue();
        subscriptionB.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_RepositoryFailure_NotDeliveredAndDoesNotThrow()
    {
        var repositoryMock = new Mock<INamespaceRepository>();
        repositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(
                Error.ExternalService("REPO_ERR", "Repository unavailable")));
        var services = new ServiceCollection();
        services.AddSingleton(repositoryMock.Object);
        var broker = new PlatformEventStreamBroker(
            services.BuildServiceProvider(),
            NullLogger<PlatformEventStreamBroker>.Instance);
        using var subscription = broker.Register(OwnerA)!;
        var evt = BuildDlqSpikeEvent(namespaceId: Guid.NewGuid());

        var act = async () => await broker.HandleAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
        subscription.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_RepositoryFailure_NegativeCachedAndNotRetriedPerEvent()
    {
        var repositoryMock = new Mock<INamespaceRepository>();
        repositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(
                Error.ExternalService("REPO_ERR", "Repository unavailable")));
        var services = new ServiceCollection();
        services.AddSingleton(repositoryMock.Object);
        var broker = new PlatformEventStreamBroker(
            services.BuildServiceProvider(),
            NullLogger<PlatformEventStreamBroker>.Instance);
        using var subscription = broker.Register(OwnerA)!;

        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: Guid.NewGuid()), CancellationToken.None);
        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: Guid.NewGuid()), CancellationToken.None);

        repositoryMock.Verify(
            r => r.GetByOwnerAsync(OwnerA, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RepositoryHangs_TimesOutInsteadOfBlockingDispatch()
    {
        // A repository that never completes must not stall the bus drain loop:
        // the lookup timeout converts the hang into the failure/fallback path.
        var repositoryMock = new Mock<INamespaceRepository>();
        repositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlySet<Guid>? _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Result<IReadOnlyList<Namespace>>.Success([]);
            });
        var services = new ServiceCollection();
        services.AddSingleton(repositoryMock.Object);
        var broker = new PlatformEventStreamBroker(
            services.BuildServiceProvider(),
            NullLogger<PlatformEventStreamBroker>.Instance);
        using var subscription = broker.Register(OwnerA)!;

        var act = async () => await broker.HandleAsync(
            BuildDlqSpikeEvent(namespaceId: Guid.NewGuid()), CancellationToken.None);

        await act.Should().NotThrowAsync();
        subscription.Reader.TryRead(out _).Should().BeFalse();
    }

    // ── Owner namespace cache ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SecondEventWithinTtl_UsesCachedNamespaceSet()
    {
        var ns = BuildNamespace(OwnerA);
        var (broker, repositoryMock) = BuildSut((OwnerA, [ns]));
        using var subscription = broker.Register(OwnerA)!;

        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: ns.Id), CancellationToken.None);
        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: ns.Id), CancellationToken.None);

        repositoryMock.Verify(
            r => r.GetByOwnerAsync(OwnerA, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NamespaceEvent_InvalidatesOwnerNamespaceCache()
    {
        var ns = BuildNamespace(OwnerA);
        var (broker, repositoryMock) = BuildSut((OwnerA, [ns]));
        using var subscription = broker.Register(OwnerA)!;

        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: ns.Id), CancellationToken.None);
        await broker.HandleAsync(BuildNamespaceCreatedEvent(actor: OwnerA), CancellationToken.None);
        await broker.HandleAsync(BuildDlqSpikeEvent(namespaceId: ns.Id), CancellationToken.None);

        repositoryMock.Verify(
            r => r.GetByOwnerAsync(OwnerA, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_UnregistersConnection_NoFurtherDelivery()
    {
        var (broker, _) = BuildSut();
        var subscription = broker.Register(OwnerA)!;

        subscription.Dispose();
        await broker.HandleAsync(BuildDlqSpikeEvent(actor: OwnerA), CancellationToken.None);

        subscription.Reader.TryRead(out _).Should().BeFalse();
        subscription.Reader.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (broker, _) = BuildSut();
        var subscription = broker.Register(OwnerA)!;

        subscription.Dispose();
        var act = () => subscription.Dispose();

        act.Should().NotThrow();
    }
}
