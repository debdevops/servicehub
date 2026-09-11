using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public class DlqOverviewServiceTests : IDisposable
{
    private const string ConnString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc123456789=";

    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _namespaces = new();
    private readonly DlqOverviewService _service;

    public DlqOverviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new DlqOverviewService(_dbContext, _namespaces.Object, new Mock<ILogger<DlqOverviewService>>().Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Namespace CreateNamespace(string name, CloudProviderType provider = CloudProviderType.Azure, EnvironmentType environment = EnvironmentType.Dev)
        => Namespace.Create(name, ConnString, ownerId: TestConstants.TestOwnerId, provider: provider, environment: environment).Value;

    private void SetOwnedNamespaces(params Namespace[] namespaces)
        => _namespaces.Setup(r => r.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Namespace>>(namespaces));

    private static DlqMessage Msg(
        Guid namespaceId,
        long seq,
        CloudProviderType provider,
        DlqMessageStatus status = DlqMessageStatus.Active,
        FailureCategory category = FailureCategory.Transient,
        string entity = "orders",
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue,
        string? topicName = null,
        DateTimeOffset? detectedAt = null,
        DateTimeOffset? replayedAt = null)
        => new()
        {
            MessageId = $"m-{seq}",
            SequenceNumber = seq,
            BodyHash = $"h-{seq}",
            NamespaceId = namespaceId,
            CloudProvider = provider,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entity,
            EntityType = entityType,
            TopicName = topicName,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-3),
            DetectedAtUtc = detectedAt ?? DateTimeOffset.UtcNow,
            DeliveryCount = 10,
            MessageSize = 128,
            FailureCategory = category,
            Status = status,
            ReplayedAt = replayedAt
        };

    [Fact]
    public async Task GetOverviewAsync_EmptyOwner_ReturnsValidation()
    {
        var result = await _service.GetOverviewAsync(" ", new DlqOverviewFilter());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DlqOverview.OwnerRequired");
    }

    [Fact]
    public async Task GetOverviewAsync_NoNamespaces_ReturnsEmptyOverview()
    {
        SetOwnedNamespaces();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter());

        result.IsSuccess.Should().BeTrue();
        result.Value.Providers.Should().BeEmpty();
        result.Value.Totals.TotalDeadLettered.Should().Be(0);
        result.Value.Totals.NamespacesTotal.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_SingleNamespaceWithActiveRows_PopulatesProviderSection()
    {
        var ns = CreateNamespace("aws-ns", CloudProviderType.Aws);
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.AddRange(
            Msg(ns.Id, 1, CloudProviderType.Aws, category: FailureCategory.ProcessingError),
            Msg(ns.Id, 2, CloudProviderType.Aws, category: FailureCategory.ProcessingError),
            Msg(ns.Id, 3, CloudProviderType.Aws, category: FailureCategory.MaxDelivery));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter());

        result.IsSuccess.Should().BeTrue();
        var provider = result.Value.Providers.Should().ContainSingle().Which;
        provider.Provider.Should().Be(CloudProviderType.Aws);
        provider.TotalDeadLettered.Should().Be(3);
        provider.NamespacesWithDlq.Should().Be(1);
        provider.NamespacesTotal.Should().Be(1);
        provider.AffectedQueues.Should().Be(1);
        provider.TopReasons.Should().ContainSingle(r => r.Category == FailureCategory.ProcessingError && r.Count == 2);
        provider.Namespaces.Should().ContainSingle().Which.DlqCount.Should().Be(3);
        provider.DailyTrend.Last().Count.Should().Be(3);

        result.Value.Totals.TotalDeadLettered.Should().Be(3);
        result.Value.Totals.OldestMessageDetectedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOverviewAsync_MultipleProviders_SortedByTotalDescending()
    {
        var awsNs = CreateNamespace("aws-ns", CloudProviderType.Aws);
        var azureNs = CreateNamespace("azure-ns", CloudProviderType.Azure);
        SetOwnedNamespaces(awsNs, azureNs);

        _dbContext.DlqMessages.Add(Msg(azureNs.Id, 1, CloudProviderType.Azure));
        for (var i = 0; i < 5; i++)
            _dbContext.DlqMessages.Add(Msg(awsNs.Id, 100 + i, CloudProviderType.Aws));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter());

        result.IsSuccess.Should().BeTrue();
        result.Value.Providers.Should().HaveCount(2);
        result.Value.Providers.First().Provider.Should().Be(CloudProviderType.Aws);
        result.Value.Providers.First().TotalDeadLettered.Should().Be(5);
        result.Value.Providers.Last().Provider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task GetOverviewAsync_ReasonFilter_NarrowsToMatchingCategory()
    {
        var ns = CreateNamespace("test-ns");
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.AddRange(
            Msg(ns.Id, 1, CloudProviderType.Azure, category: FailureCategory.DataQuality),
            Msg(ns.Id, 2, CloudProviderType.Azure, category: FailureCategory.Transient));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(
            TestConstants.TestOwnerId,
            new DlqOverviewFilter(Reason: FailureCategory.DataQuality));

        result.IsSuccess.Should().BeTrue();
        result.Value.Totals.TotalDeadLettered.Should().Be(1);
        result.Value.Providers.Single().TopReasons.Should().ContainSingle(r => r.Category == FailureCategory.DataQuality);
    }

    [Fact]
    public async Task GetOverviewAsync_EnvironmentFilter_ExcludesOtherEnvironments()
    {
        var devNs = CreateNamespace("dev-ns", environment: EnvironmentType.Dev);
        var prodNs = CreateNamespace("prod-ns", environment: EnvironmentType.Prod);
        SetOwnedNamespaces(devNs, prodNs);

        _dbContext.DlqMessages.Add(Msg(devNs.Id, 1, CloudProviderType.Azure));
        _dbContext.DlqMessages.Add(Msg(prodNs.Id, 2, CloudProviderType.Azure));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(
            TestConstants.TestOwnerId,
            new DlqOverviewFilter(Environment: EnvironmentType.Prod));

        result.IsSuccess.Should().BeTrue();
        result.Value.Totals.NamespacesTotal.Should().Be(1);
        result.Value.Providers.Single().Namespaces.Should().ContainSingle().Which.NamespaceName.Should().Be("prod-ns");
    }

    [Fact]
    public async Task GetOverviewAsync_NamespaceWithOnlyReplayedRows_CountsTowardTotalNotListed()
    {
        var ns = CreateNamespace("test-ns");
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.Add(Msg(ns.Id, 1, CloudProviderType.Azure, status: DlqMessageStatus.Replayed, replayedAt: DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter());

        result.IsSuccess.Should().BeTrue();
        var provider = result.Value.Providers.Should().ContainSingle().Which;
        provider.TotalDeadLettered.Should().Be(0);
        provider.NamespacesTotal.Should().Be(1);
        provider.Namespaces.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_SubscriptionRows_CountTopicsByTopicName()
    {
        var ns = CreateNamespace("test-ns");
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.AddRange(
            Msg(ns.Id, 1, CloudProviderType.Azure, entity: "sub-a", entityType: ServiceBusEntityType.Subscription, topicName: "orders-topic"),
            Msg(ns.Id, 2, CloudProviderType.Azure, entity: "sub-b", entityType: ServiceBusEntityType.Subscription, topicName: "orders-topic"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter());

        result.IsSuccess.Should().BeTrue();
        var provider = result.Value.Providers.Should().ContainSingle().Which;
        provider.AffectedTopics.Should().Be(1);
        provider.AffectedQueues.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_NewBacklogWithNoPriorHistory_ChangePercentIsNull()
    {
        var ns = CreateNamespace("test-ns");
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.Add(Msg(ns.Id, 1, CloudProviderType.Azure, detectedAt: DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, new DlqOverviewFilter(Days: 7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Providers.Single().ChangePercent.Should().BeNull();
    }
}
