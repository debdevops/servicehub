using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryExternalSignalCorrelationCacheTests
{
    private readonly InMemoryExternalSignalCorrelationCache _sut = new();

    private static ExternalSignalCorrelation CreateCorrelation()
    {
        var signal = new ExternalSignalEvent
        {
            OwnerId = "entra:owner-a",
            SignalType = ExternalSignalType.Deploy,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Source = "manual",
            IngestedAt = DateTimeOffset.UtcNow,
        };

        return ExternalSignalCorrelation.Create(
            "entra:owner-a",
            Guid.NewGuid(),
            "queue-1",
            AnomalyType.HighMessageVolume,
            80,
            CloudProviderType.Azure,
            signal,
            TimeSpan.FromMinutes(10),
            "spike after deploy");
    }

    [Fact]
    public void TryGet_UnknownId_ReturnsNull()
    {
        _sut.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Store_ThenTryGet_ReturnsTheSameCorrelation()
    {
        var correlation = CreateCorrelation();

        _sut.Store(new[] { correlation });

        _sut.TryGet(correlation.Id).Should().BeSameAs(correlation);
    }

    [Fact]
    public void Store_MultipleCorrelations_AllRetrievable()
    {
        var c1 = CreateCorrelation();
        var c2 = CreateCorrelation();

        _sut.Store(new[] { c1, c2 });

        _sut.TryGet(c1.Id).Should().BeSameAs(c1);
        _sut.TryGet(c2.Id).Should().BeSameAs(c2);
    }

    [Fact]
    public void Store_NullArgument_Throws()
    {
        var act = () => _sut.Store(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
