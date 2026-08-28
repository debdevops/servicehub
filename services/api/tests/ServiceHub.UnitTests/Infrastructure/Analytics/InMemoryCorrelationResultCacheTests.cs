using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryCorrelationResultCacheTests
{
    private readonly InMemoryCorrelationResultCache _sut = new();

    private static CorrelationFinding CreateFinding() =>
        CorrelationFinding.Create(
            "key_testowner",
            CloudProviderType.Azure,
            new[]
            {
                new CorrelationMember(Guid.NewGuid(), "queue-1", AnomalyType.HighMessageVolume, 80),
                new CorrelationMember(Guid.NewGuid(), "queue-2", AnomalyType.HighMessageVolume, 70),
            },
            80,
            "correlated spike");

    [Fact]
    public void TryGet_UnknownId_ReturnsNull()
    {
        _sut.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Store_ThenTryGet_ReturnsTheSameFinding()
    {
        var finding = CreateFinding();

        _sut.Store(new[] { finding });

        _sut.TryGet(finding.Id).Should().BeSameAs(finding);
    }

    [Fact]
    public void Store_MultipleFindings_AllRetrievable()
    {
        var f1 = CreateFinding();
        var f2 = CreateFinding();

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
