using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Coverage for <see cref="PreventionRuleExpiryWorker"/>'s thin timer-loop wrapper — constructor
/// guards and graceful start/stop, mirroring <see cref="PlaybookExpiryWorkerTests"/>'s own bar for
/// a background-service loop this shallow. The actual expiry/revocation logic
/// (<c>PreventionRuleEvaluationService.SweepExpiredRulesAsync</c>) is unit-tested directly in
/// <c>PreventionRuleEvaluationServiceTests</c> — duplicating that coverage here through the
/// service-locator/timer plumbing would test the wiring twice and the behavior nowhere better.
/// </summary>
public sealed class PreventionRuleExpiryWorkerTests
{
    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        var act = () => new PreventionRuleExpiryWorker(
            null!, new ConfigurationBuilder().Build(), NullLogger<PreventionRuleExpiryWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new PreventionRuleExpiryWorker(
            Mock.Of<IServiceProvider>(), null!, NullLogger<PreventionRuleExpiryWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new PreventionRuleExpiryWorker(
            Mock.Of<IServiceProvider>(), new ConfigurationBuilder().Build(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        var repoMock = new Mock<INamespaceRepository>();
        repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var evaluationMock = new Mock<IPreventionRuleEvaluationService>();

        var services = new ServiceCollection();
        services.AddSingleton(repoMock.Object);
        services.AddSingleton(evaluationMock.Object);
        var provider = services.BuildServiceProvider();

        var worker = new PreventionRuleExpiryWorker(
            provider, new ConfigurationBuilder().Build(), NullLogger<PreventionRuleExpiryWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
