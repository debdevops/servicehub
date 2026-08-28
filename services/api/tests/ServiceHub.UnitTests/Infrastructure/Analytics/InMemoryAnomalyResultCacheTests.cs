using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryAnomalyResultCacheTests
{
    private readonly InMemoryAnomalyResultCache _sut = new();

    private static Anomaly CreateAnomaly() =>
        Anomaly.Create(Guid.NewGuid(), "test-queue", AnomalyType.HighMessageVolume, 90, "spike");

    [Fact]
    public void TryGet_UnknownId_ReturnsNull()
    {
        _sut.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Store_ThenTryGet_ReturnsTheSameAnomaly()
    {
        var anomaly = CreateAnomaly();

        _sut.Store(new[] { anomaly });

        _sut.TryGet(anomaly.Id).Should().BeSameAs(anomaly);
    }

    [Fact]
    public void Store_MultipleAnomalies_AllRetrievable()
    {
        var a1 = CreateAnomaly();
        var a2 = CreateAnomaly();

        _sut.Store(new[] { a1, a2 });

        _sut.TryGet(a1.Id).Should().BeSameAs(a1);
        _sut.TryGet(a2.Id).Should().BeSameAs(a2);
    }

    [Fact]
    public void Store_NullArgument_Throws()
    {
        var act = () => _sut.Store(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
