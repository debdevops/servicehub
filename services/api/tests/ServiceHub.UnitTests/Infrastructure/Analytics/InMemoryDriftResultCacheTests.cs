using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryDriftResultCacheTests
{
    private readonly InMemoryDriftResultCache _sut = new();

    private static DriftFinding CreateFinding() =>
        DriftFinding.Create(Guid.NewGuid(), "test-queue", DriftFindingType.SchemaShapeDrift, 60, "shape drift");

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
