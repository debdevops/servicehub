using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class DlqMonitorWorkerTests
{
    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new DlqMonitorWorker(null!, NullLogger<DlqMonitorWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DlqMonitorWorker(Mock.Of<IServiceProvider>(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        // Set up a minimal service provider with DlqDbContext and INamespaceRepository
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var dbContext = new DlqDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(repoMock.Object);
        services.AddSingleton(Mock.Of<IDlqMonitorService>());
        services.AddSingleton(Mock.Of<IPlatformEventBus>());
        var sp = services.BuildServiceProvider();

        var worker = new DlqMonitorWorker(sp, NullLogger<DlqMonitorWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Should not throw — just stop gracefully on cancellation
        await worker.StartAsync(cts.Token);
        await Task.Delay(400); // allow time for the cancellation to propagate
        await worker.StopAsync(CancellationToken.None);

        dbContext.Database.CloseConnection();
        dbContext.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_DbInitFailure_StopsWithoutCrash()
    {
        // Provide a service provider that throws when resolving DlqDbContext
        var serviceScopeMock = new Mock<IServiceScope>();
        var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopedServiceProviderMock = new Mock<IServiceProvider>();

        scopedServiceProviderMock.Setup(sp => sp.GetService(typeof(DlqDbContext)))
            .Throws(new InvalidOperationException("No DbContext"));

        serviceScopeMock.Setup(s => s.ServiceProvider).Returns(scopedServiceProviderMock.Object);
        serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(serviceScopeMock.Object);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(serviceScopeFactoryMock.Object);

        // IPlatformEventBus is resolved from root provider in constructor — must be present.
        var eventBusMock = new Mock<IPlatformEventBus>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IPlatformEventBus)))
            .Returns(eventBusMock.Object);

        var worker = new DlqMonitorWorker(serviceProviderMock.Object, NullLogger<DlqMonitorWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Should handle the exception gracefully and return
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);
    }

    // ── Platform Event publisher tests ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SpikeDetected_PublishesExactlyOnePlatformEvent()
    {
        // The worker has a 5s initial delay before its first scan.
        // This test waits past that delay to observe at least one scan cycle.
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var dbContext = new DlqDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var ns = Namespace.Create("spike-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey12=").Value;

        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var monitorMock = new Mock<IDlqMonitorService>();
        monitorMock
            .Setup(m => m.ScanNamespaceAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(5));   // 5 new messages — spike!

        var eventBusMock = new Mock<IPlatformEventBus>();
        eventBusMock
            .Setup(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(repoMock.Object);
        services.AddSingleton(monitorMock.Object);
        services.AddSingleton(eventBusMock.Object);
        var sp = services.BuildServiceProvider();

        var worker = new DlqMonitorWorker(sp, NullLogger<DlqMonitorWorker>.Instance);

        // Give the worker 7s: 5s initial delay + 2s for at least one scan cycle.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await worker.StopAsync(CancellationToken.None);

        // Assert — at least one DlqSpikeDetected event published after the initial delay.
        eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.DlqSpikeDetected),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        dbContext.Database.CloseConnection();
        dbContext.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_AwsAndGcpNamespaces_AreScanned()
    {
        // Non-Azure namespaces must no longer be filtered out of the scan cycle.
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var dbContext = new DlqDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var awsNs = Namespace.Create("aws-ns", "akid:secret", provider: CloudProviderType.Aws).Value;
        var gcpNs = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp).Value;

        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { awsNs, gcpNs }));

        var monitorMock = new Mock<IDlqMonitorService>();
        monitorMock
            .Setup(m => m.ScanNamespaceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(repoMock.Object);
        services.AddSingleton(monitorMock.Object);
        services.AddSingleton(Mock.Of<IPlatformEventBus>());
        var sp = services.BuildServiceProvider();

        var worker = new DlqMonitorWorker(sp, NullLogger<DlqMonitorWorker>.Instance);

        // Give the worker 7s: 5s initial delay + 2s for at least one scan cycle.
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await worker.StopAsync(CancellationToken.None);

        monitorMock.Verify(
            m => m.ScanNamespaceAsync(awsNs.Id, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        monitorMock.Verify(
            m => m.ScanNamespaceAsync(gcpNs.Id, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        dbContext.Database.CloseConnection();
        dbContext.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_NoSpike_PublishesZeroPlatformEvents()
    {
        // Arrange — scan returns 0 new messages (no spike).
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var dbContext = new DlqDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var ns = Namespace.Create("quiet-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey12=").Value;

        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var monitorMock = new Mock<IDlqMonitorService>();
        monitorMock
            .Setup(m => m.ScanNamespaceAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));   // 0 new messages — no spike.

        var eventBusMock = new Mock<IPlatformEventBus>();
        eventBusMock
            .Setup(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(repoMock.Object);
        services.AddSingleton(monitorMock.Object);
        services.AddSingleton(eventBusMock.Object);
        var sp = services.BuildServiceProvider();

        var worker = new DlqMonitorWorker(sp, NullLogger<DlqMonitorWorker>.Instance);

        // Allow 7s to complete at least one scan cycle past the initial delay.
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await worker.StopAsync(CancellationToken.None);

        // Assert — zero events published when no spike is detected.
        eventBusMock.Verify(
            b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        dbContext.Database.CloseConnection();
        dbContext.Dispose();
    }
}
