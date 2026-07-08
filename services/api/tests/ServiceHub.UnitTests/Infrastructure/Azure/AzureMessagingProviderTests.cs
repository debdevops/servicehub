using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Azure;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Azure;

/// <summary>
/// Tests for <see cref="AzureMessagingProvider"/>.
/// </summary>
public sealed class AzureMessagingProviderTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string ProtectedConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=";
    private const string PlainConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=xyz=";

    private static Namespace BuildNamespace() =>
        Namespace.Create("test-azure-ns", ProtectedConnectionString).Value;

    private static AzureMessagingProvider BuildProvider(
        IServiceBusClientFactory? clientFactory = null,
        IMessageReceiver? receiver = null,
        IMessageSender? sender = null,
        INamespaceRepository? repo = null,
        IConnectionStringProtector? protector = null,
        IServiceBusClientCache? clientCache = null)
    {
        return new AzureMessagingProvider(
            clientFactory ?? new Mock<IServiceBusClientFactory>().Object,
            receiver ?? new Mock<IMessageReceiver>().Object,
            sender ?? new Mock<IMessageSender>().Object,
            repo ?? new Mock<INamespaceRepository>().Object,
            protector ?? new Mock<IConnectionStringProtector>().Object,
            clientCache ?? new Mock<IServiceBusClientCache>().Object,
            NullLogger<AzureMessagingProvider>.Instance);
    }

    private static Mock<INamespaceRepository> BuildRepo(Namespace ns)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        return repo;
    }

    private static Mock<IConnectionStringProtector> BuildProtector()
    {
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success(PlainConnectionString));
        return protector;
    }

    private static QueueRuntimePropertiesDto BuildQueue(string name, long active, long dlq) =>
        new(
            Name: name,
            ActiveMessageCount: active,
            DeadLetterMessageCount: dlq,
            ScheduledMessageCount: 0,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            SizeInBytes: 1024,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue);

    private static TopicRuntimePropertiesDto BuildTopic(string name) =>
        new(
            Name: name,
            SubscriptionCount: 1,
            SizeInBytes: 2048,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            SupportOrdering: false,
            MaxSizeInMegabytes: 1024,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));

    private static SubscriptionRuntimePropertiesDto BuildSubscription(
        string topicName, string name, long active, long dlq) =>
        new(
            Name: name,
            TopicName: topicName,
            ActiveMessageCount: active,
            DeadLetterMessageCount: dlq,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: false,
            EnableDeadLetteringOnFilterEvaluationExceptions: true,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: null,
            ForwardDeadLetteredMessagesTo: null);

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullClientFactory_Throws()
    {
        var act = () => new AzureMessagingProvider(
            null!,
            new Mock<IMessageReceiver>().Object,
            new Mock<IMessageSender>().Object,
            new Mock<INamespaceRepository>().Object,
            new Mock<IConnectionStringProtector>().Object,
            new Mock<IServiceBusClientCache>().Object,
            NullLogger<AzureMessagingProvider>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullClientCache_Throws()
    {
        var act = () => new AzureMessagingProvider(
            new Mock<IServiceBusClientFactory>().Object,
            new Mock<IMessageReceiver>().Object,
            new Mock<IMessageSender>().Object,
            new Mock<INamespaceRepository>().Object,
            new Mock<IConnectionStringProtector>().Object,
            null!,
            NullLogger<AzureMessagingProvider>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var act = () => BuildProvider();
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ProviderType / receiver / sender
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_ReturnsAzure()
    {
        BuildProvider().ProviderType.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public void GetMessageReceiver_ReturnsReceiverInstance()
    {
        var receiver = new Mock<IMessageReceiver>().Object;
        BuildProvider(receiver: receiver).GetMessageReceiver().Should().BeSameAs(receiver);
    }

    [Fact]
    public void GetMessageSender_ReturnsSenderInstance()
    {
        var sender = new Mock<IMessageSender>().Object;
        BuildProvider(sender: sender).GetMessageSender().Should().BeSameAs(sender);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ValidateConnectionAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateConnectionAsync_NullNamespace_Throws()
    {
        var provider = BuildProvider();
        var act = async () => await provider.ValidateConnectionAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateConnectionAsync_DelegatesToClientFactory()
    {
        var ns = BuildNamespace();
        var factory = new Mock<IServiceBusClientFactory>();
        factory.Setup(f => f.CreateClientAsync(ns, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var provider = BuildProvider(clientFactory: factory.Object);

        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.Verify(f => f.CreateClientAsync(ns, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesAsync — failure paths
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(
                Error.NotFound(ErrorCodes.Namespace.NotFound, "not found")));

        var provider = BuildProvider(repo: repo.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task ListEntitiesAsync_WhenNoConnectionString_ReturnsValidationFailure()
    {
        var ns = Namespace.CreateWithManagedIdentity("test-mi-ns").Value;
        var provider = BuildProvider(repo: BuildRepo(ns).Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Namespace.ConnectionStringRequired);
    }

    [Fact]
    public async Task ListEntitiesAsync_WhenUnprotectFails_ReturnsFailure()
    {
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Failure(
                Error.Validation("Encryption.DecryptFailed", "bad key")));

        var provider = BuildProvider(
            repo: BuildRepo(BuildNamespace()).Object,
            protector: protector.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.DecryptFailed");
    }

    [Fact]
    public async Task ListEntitiesAsync_WhenGetQueuesFails_ReturnsFailure()
    {
        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Failure(
                Error.ExternalService(ErrorCodes.Queue.ListFailed, "boom")));

        var cache = new Mock<IServiceBusClientCache>();
        cache.Setup(c => c.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(wrapper.Object);

        var provider = BuildProvider(
            repo: BuildRepo(BuildNamespace()).Object,
            protector: BuildProtector().Object,
            clientCache: cache.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Queue.ListFailed);
    }

    [Fact]
    public async Task ListEntitiesAsync_WhenClientCacheThrows_ReturnsExternalServiceFailure()
    {
        var cache = new Mock<IServiceBusClientCache>();
        cache.Setup(c => c.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("connection refused"));

        var provider = BuildProvider(
            repo: BuildRepo(BuildNamespace()).Object,
            protector: BuildProtector().Object,
            clientCache: cache.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ErrorCodes.Namespace.ConnectionFailed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesAsync — success mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WithQueuesTopicsAndSubscriptions_ReturnsMappedEntities()
    {
        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                [BuildQueue("orders", active: 5, dlq: 2)]));
        wrapper.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                [BuildTopic("events")]));
        wrapper.Setup(w => w.GetSubscriptionsAsync("events", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(
                [BuildSubscription("events", "audit", active: 7, dlq: 3)]));

        var cache = new Mock<IServiceBusClientCache>();
        cache.Setup(c => c.GetOrCreate(It.IsAny<Guid>(), PlainConnectionString))
            .Returns(wrapper.Object);

        var provider = BuildProvider(
            repo: BuildRepo(BuildNamespace()).Object,
            protector: BuildProtector().Object,
            clientCache: cache.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        var queue = result.Value.Single(e => e.EntityType == "Queue");
        queue.Name.Should().Be("orders");
        queue.ActiveMessageCount.Should().Be(5);
        queue.DeadLetterCount.Should().Be(2);
        queue.Provider.Should().Be(CloudProviderType.Azure);

        var topic = result.Value.Single(e => e.EntityType == "Topic");
        topic.Name.Should().Be("events");
        topic.Provider.Should().Be(CloudProviderType.Azure);

        var subscription = result.Value.Single(e => e.EntityType == "Subscription");
        subscription.Name.Should().Be("events/subscriptions/audit");
        subscription.ActiveMessageCount.Should().Be(7);
        subscription.DeadLetterCount.Should().Be(3);
        subscription.Provider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task ListEntitiesAsync_WithNoEntities_ReturnsEmptyList()
    {
        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success([]));
        wrapper.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success([]));

        var cache = new Mock<IServiceBusClientCache>();
        cache.Setup(c => c.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(wrapper.Object);

        var provider = BuildProvider(
            repo: BuildRepo(BuildNamespace()).Object,
            protector: BuildProtector().Object,
            clientCache: cache.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
