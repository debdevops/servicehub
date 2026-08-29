using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class BacklogForecastWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IBacklogForecastService> _forecastMock = new();
    private readonly Mock<IBacklogForecastResultCache> _cacheMock = new();
    private readonly Mock<IPlatformEventBus> _eventBusMock = new();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static IConfiguration ConfigWithPushThreshold(int threshold) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Insight:PushSeverityThreshold"] = threshold.ToString() })
            .Build();

    private IServiceProvider BuildServiceProvider(bool registerEventBus = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repoMock.Object);
        services.AddSingleton(_forecastMock.Object);
        services.AddSingleton(_cacheMock.Object);
        if (registerEventBus)
        {
            services.AddSingleton(_eventBusMock.Object);
        }
        return services.BuildServiceProvider();
    }

    private static Namespace CreateTestNamespace() =>
        Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    private static BacklogForecast CreateForecast(Guid namespaceId, string entityName, int severity) =>
        BacklogForecast.Create(namespaceId, entityName, 80, 10, 150, 7, severity, "projected breach");

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new BacklogForecastWorker(null!, EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new BacklogForecastWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<BacklogForecastWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RunForecastCycleAsync ───────────────────────────────────────

    [Fact]
    public async Task RunForecastCycleAsync_NoActiveNamespaces_DoesNotCallForecast()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _forecastMock.Verify(
            f => f.ForecastAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunForecastCycleAsync_GetActiveNamespacesFails_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        var act = () => worker.RunForecastCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _forecastMock.Verify(
            f => f.ForecastAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunForecastCycleAsync_ForecastsFound_StoresThemInCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var forecast = CreateForecast(ns.Id, "queue-1", 80);
        _forecastMock.Setup(f => f.ForecastAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new[] { forecast }));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<BacklogForecast>>(f => f.Contains(forecast))), Times.Once);
    }

    [Fact]
    public async Task RunForecastCycleAsync_NoForecastsFound_DoesNotTouchCache()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _forecastMock.Setup(f => f.ForecastAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(Array.Empty<BacklogForecast>()));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.IsAny<IEnumerable<BacklogForecast>>()), Times.Never);
    }

    [Fact]
    public async Task RunForecastCycleAsync_ForecastFailsForOneNamespace_ContinuesWithOthers()
    {
        var failingNs = CreateTestNamespace();
        var okNs = Namespace.Create(
            "test-namespace-2",
            "Endpoint=sb://test2.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS 2").Value;

        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { failingNs, okNs }));

        _forecastMock.Setup(f => f.ForecastAsync(failingNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Failure(Error.Internal("ERR", "boom")));

        var forecast = CreateForecast(okNs.Id, "queue-2", 40);
        _forecastMock.Setup(f => f.ForecastAsync(okNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new[] { forecast }));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _cacheMock.Verify(c => c.Store(It.Is<IEnumerable<BacklogForecast>>(f => f.Contains(forecast))), Times.Once);
    }

    // ── Roadmap §5, I5 — Push ────────────────────────────────────────

    [Fact]
    public async Task RunForecastCycleAsync_ForecastAtOrAboveThreshold_PublishesInsightDetectedEvent()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var forecast = CreateForecast(ns.Id, "queue-1", 80);
        _forecastMock.Setup(f => f.ForecastAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new[] { forecast }));

        var worker = new BacklogForecastWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.InsightDetected && e.NamespaceId == ns.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunForecastCycleAsync_ForecastBelowThreshold_DoesNotPublish()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var forecast = CreateForecast(ns.Id, "queue-1", 40);
        _forecastMock.Setup(f => f.ForecastAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new[] { forecast }));

        var worker = new BacklogForecastWorker(BuildServiceProvider(registerEventBus: true), ConfigWithPushThreshold(70), NullLogger<BacklogForecastWorker>.Instance);

        await worker.RunForecastCycleAsync(CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunForecastCycleAsync_NoEventBusRegistered_DoesNotThrow()
    {
        var ns = CreateTestNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var forecast = CreateForecast(ns.Id, "queue-1", 95);
        _forecastMock.Setup(f => f.ForecastAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new[] { forecast }));

        var worker = new BacklogForecastWorker(BuildServiceProvider(registerEventBus: false), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        var act = () => worker.RunForecastCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new BacklogForecastWorker(BuildServiceProvider(), EmptyConfig(), NullLogger<BacklogForecastWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
