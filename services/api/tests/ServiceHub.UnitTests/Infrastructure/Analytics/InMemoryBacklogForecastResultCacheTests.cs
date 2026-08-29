using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryBacklogForecastResultCacheTests
{
    private readonly InMemoryBacklogForecastResultCache _sut = new();

    private static BacklogForecast CreateForecast() =>
        BacklogForecast.Create(Guid.NewGuid(), "test-queue", 80, 10, 150, 7, 60, "projected breach");

    [Fact]
    public void TryGet_UnknownId_ReturnsNull()
    {
        _sut.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Store_ThenTryGet_ReturnsTheSameForecast()
    {
        var forecast = CreateForecast();

        _sut.Store(new[] { forecast });

        _sut.TryGet(forecast.Id).Should().BeSameAs(forecast);
    }

    [Fact]
    public void Store_MultipleForecasts_AllRetrievable()
    {
        var f1 = CreateForecast();
        var f2 = CreateForecast();

        _sut.Store(new[] { f1, f2 });

        _sut.TryGet(f1.Id).Should().BeSameAs(f1);
        _sut.TryGet(f2.Id).Should().BeSameAs(f2);
    }

    [Fact]
    public void Store_NullArgument_Throws()
    {
        var act = () => _sut.Store(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
