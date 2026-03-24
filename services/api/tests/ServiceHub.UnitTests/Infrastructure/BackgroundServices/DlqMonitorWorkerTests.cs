using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
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

        var worker = new DlqMonitorWorker(serviceProviderMock.Object, NullLogger<DlqMonitorWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Should handle the exception gracefully and return
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);
    }
}
