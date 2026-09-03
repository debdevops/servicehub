using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public class FleetOverviewServiceTests : IDisposable
{
    private const string ConnString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc123456789=";

    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _namespaces = new();
    private readonly FleetOverviewService _service;

    public FleetOverviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        // Default: Azure registered, no config overrides — every existing test below uses the
        // default (Azure) namespace, which is always Scanned.
        _service = CreateService(registeredProviders: CloudProviderType.Azure);
    }

    /// <summary>
    /// Builds a service wired with only the registered providers listed, mirroring
    /// <c>DlqMonitorServiceTests.CreateSut</c>'s pattern for router/configuration setup.
    /// </summary>
    private FleetOverviewService CreateService(
        IReadOnlyDictionary<string, string?>? configData = null,
        params CloudProviderType[] registeredProviders)
    {
        var providers = registeredProviders.Select(p =>
        {
            var mock = new Mock<ICloudMessagingProvider>();
            mock.SetupGet(x => x.ProviderType).Returns(p);
            mock.SetupGet(x => x.Capabilities).Returns(p switch
            {
                CloudProviderType.Aws => ProviderCapabilities.Aws,
                CloudProviderType.Gcp => ProviderCapabilities.Gcp,
                _ => ProviderCapabilities.Azure,
            });
            return mock.Object;
        });
        var router = new CloudProviderRouter(providers);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData ?? new Dictionary<string, string?>())
            .Build();

        return new FleetOverviewService(
            _dbContext, _namespaces.Object, router, configuration,
            new Mock<ILogger<FleetOverviewService>>().Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    private Namespace CreateNamespace(string name)
        => Namespace.Create(name, ConnString, ownerId: TestConstants.TestOwnerId).Value;

    private void SetOwnedNamespaces(params Namespace[] namespaces)
        => _namespaces.Setup(r => r.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Namespace>>(namespaces));

    private DlqMessage Msg(
        Guid namespaceId,
        long seq,
        DlqMessageStatus status = DlqMessageStatus.Active,
        FailureCategory category = FailureCategory.Transient,
        string entity = "orders",
        DateTimeOffset? detectedAt = null)
        => new()
        {
            MessageId = $"m-{seq}",
            SequenceNumber = seq,
            BodyHash = $"h-{seq}",
            NamespaceId = namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entity,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-3),
            DetectedAtUtc = detectedAt ?? DateTimeOffset.UtcNow,
            DeliveryCount = 10,
            MessageSize = 128,
            FailureCategory = category,
            Status = status
        };

    [Fact]
    public async Task GetOverviewAsync_EmptyOwner_ReturnsValidation()
    {
        var result = await _service.GetOverviewAsync(" ");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Fleet.OwnerRequired");
    }

    [Fact]
    public async Task GetOverviewAsync_NoData_ReturnsHealthyNamespaces()
    {
        var ns = CreateNamespace("healthy-ns");
        SetOwnedNamespaces(ns);

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.NamespaceCount.Should().Be(1);
        result.Value.TotalActive.Should().Be(0);
        result.Value.Namespaces.Should().ContainSingle()
            .Which.Severity.Should().Be(FleetHealthSeverity.Healthy);
        result.Value.Namespaces.Single().Coverage.Should().Be(FleetMonitoringCoverage.Scanned);
    }

    [Fact]
    public async Task GetOverviewAsync_UnmonitoredAwsNamespace_NoRows_IsUnknownNeverHealthy()
    {
        var ns = Namespace.Create("aws-ns", "akid:secret", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Aws).Value;
        SetOwnedNamespaces(ns);

        // AWS registered, but no DlqMonitor:AllowDestructivePeek:Aws override — matches the
        // shipped default (appsettings.json: AllowDestructivePeek.Aws = false).
        var service = CreateService(registeredProviders: CloudProviderType.Aws);

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Unknown);
        health.Severity.Should().NotBe(FleetHealthSeverity.Healthy);
        health.Coverage.Should().Be(FleetMonitoringCoverage.NotMonitored);
        health.CoverageNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_AwsNamespace_AllowDestructivePeekEnabled_IsScannedAndHealthy()
    {
        var ns = Namespace.Create("aws-ns", "akid:secret", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Aws).Value;
        SetOwnedNamespaces(ns);

        var service = CreateService(
            configData: new Dictionary<string, string?> { ["DlqMonitor:AllowDestructivePeek:Aws"] = "true" },
            registeredProviders: CloudProviderType.Aws);

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Healthy);
        health.Coverage.Should().Be(FleetMonitoringCoverage.Scanned);
        health.CoverageNote.Should().BeNull();
    }

    [Fact]
    public async Task GetOverviewAsync_UnmonitoredGcpNamespace_NoRows_IsUnknownNeverHealthy()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Gcp).Value;
        SetOwnedNamespaces(ns);

        // GCP registered, but no DlqMonitor:AllowDestructivePeek:Gcp entry in appsettings.json
        // at all — GetValue falls back to its `false` default, same effective behavior as AWS.
        var service = CreateService(registeredProviders: CloudProviderType.Gcp);

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Unknown);
        health.Severity.Should().NotBe(FleetHealthSeverity.Healthy);
        health.Coverage.Should().Be(FleetMonitoringCoverage.NotMonitored);
        health.CoverageNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_GcpNamespace_AllowDestructivePeekEnabled_IsScannedAndHealthy()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Gcp).Value;
        SetOwnedNamespaces(ns);

        var service = CreateService(
            configData: new Dictionary<string, string?> { ["DlqMonitor:AllowDestructivePeek:Gcp"] = "true" },
            registeredProviders: CloudProviderType.Gcp);

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Healthy);
        health.Coverage.Should().Be(FleetMonitoringCoverage.Scanned);
        health.CoverageNote.Should().BeNull();
    }

    [Fact]
    public async Task GetOverviewAsync_UnmonitoredAwsNamespace_WithKnownActiveRows_KeepsRealSeverity()
    {
        // A confirmed backlog (from before monitoring was disabled, or a historical scan) must
        // never be suppressed just because the namespace isn't currently monitored.
        var ns = Namespace.Create("aws-ns", "akid:secret", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Aws).Value;
        SetOwnedNamespaces(ns);
        _dbContext.DlqMessages.Add(Msg(ns.Id, 1));
        await _dbContext.SaveChangesAsync();

        var service = CreateService(registeredProviders: CloudProviderType.Aws);

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Warning);
        health.Coverage.Should().Be(FleetMonitoringCoverage.NotMonitored);
    }

    [Fact]
    public async Task GetOverviewAsync_ProviderNotRegistered_IsUnknownWithProviderNotRegisteredCoverage()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", ownerId: TestConstants.TestOwnerId, provider: CloudProviderType.Gcp).Value;
        SetOwnedNamespaces(ns);

        // No providers registered at all — mirrors CloudProviders:Gcp:Enabled=false.
        var service = CreateService();

        var result = await service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        var health = result.Value.Namespaces.Should().ContainSingle().Which;
        health.Severity.Should().Be(FleetHealthSeverity.Unknown);
        health.Coverage.Should().Be(FleetMonitoringCoverage.ProviderNotRegistered);
        health.CoverageNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_AggregatesActiveAcrossNamespaces()
    {
        var ns1 = CreateNamespace("ns-one");
        var ns2 = CreateNamespace("ns-two");
        SetOwnedNamespaces(ns1, ns2);

        _dbContext.DlqMessages.AddRange(
            Msg(ns1.Id, 1, entity: "orders"),
            Msg(ns1.Id, 2, entity: "orders"),
            Msg(ns2.Id, 3, entity: "payments"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalActive.Should().Be(3);
        var ns1Health = result.Value.Namespaces.Single(n => n.NamespaceId == ns1.Id);
        ns1Health.ActiveCount.Should().Be(2);
        ns1Health.TopEntity.Should().Be("orders");
        ns1Health.TopEntityCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_LargeBacklog_IsCriticalAndSortedFirst()
    {
        var quiet = CreateNamespace("quiet-ns");
        var noisy = CreateNamespace("noisy-ns");
        SetOwnedNamespaces(quiet, noisy);

        _dbContext.DlqMessages.Add(Msg(quiet.Id, 1));
        for (var i = 0; i < 55; i++)
            _dbContext.DlqMessages.Add(Msg(noisy.Id, 100 + i));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Namespaces.First().NamespaceId.Should().Be(noisy.Id);
        result.Value.Namespaces.First().Severity.Should().Be(FleetHealthSeverity.Critical);
    }

    [Fact]
    public async Task GetOverviewAsync_NewInWindow_CountsOnlyRecent()
    {
        var ns = CreateNamespace("window-ns");
        SetOwnedNamespaces(ns);

        _dbContext.DlqMessages.Add(Msg(ns.Id, 1, detectedAt: DateTimeOffset.UtcNow.AddHours(-2)));   // in 24h window
        _dbContext.DlqMessages.Add(Msg(ns.Id, 2, detectedAt: DateTimeOffset.UtcNow.AddDays(-5)));     // outside
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId, windowHours: 24);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalNewInWindow.Should().Be(1);
        result.Value.DailyTrend.Should().HaveCount(7);
    }

    [Fact]
    public async Task GetOverviewAsync_OrphanNamespaceData_IsExcludedFromRollup()
    {
        // A namespace that no longer exists must never surface as a "(deleted namespace)"
        // row or inflate NamespaceCount — its DLQ rows stay intact in SQLite (nothing here
        // deletes them), they simply aren't part of the live fleet rollup anymore.
        SetOwnedNamespaces(); // owner currently has no namespaces registered
        var deletedNsId = Guid.NewGuid();
        _dbContext.DlqMessages.Add(Msg(deletedNsId, 1));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOverviewAsync(TestConstants.TestOwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Namespaces.Should().BeEmpty();
        result.Value.NamespaceCount.Should().Be(0);

        // The underlying row is untouched — still queryable directly, proving nothing was deleted.
        (await _dbContext.DlqMessages.CountAsync(m => m.NamespaceId == deletedNsId)).Should().Be(1);
    }
}
