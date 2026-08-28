using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class InMemoryNarrationResultCacheTests
{
    private readonly InMemoryNarrationResultCache _sut = new();

    private static Narration CreateNarration() =>
        Narration.Create(
            NarrationKind.NamespaceActivity,
            Guid.NewGuid(),
            [Guid.NewGuid()],
            "headline",
            "summary",
            80);

    [Fact]
    public void TryGet_UnknownId_ReturnsNull()
    {
        _sut.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Store_ThenTryGet_ReturnsTheSameNarration()
    {
        var narration = CreateNarration();

        _sut.Store(new[] { narration });

        _sut.TryGet(narration.Id).Should().BeSameAs(narration);
    }

    [Fact]
    public void Store_MultipleNarrations_AllRetrievable()
    {
        var n1 = CreateNarration();
        var n2 = CreateNarration();

        _sut.Store(new[] { n1, n2 });

        _sut.TryGet(n1.Id).Should().BeSameAs(n1);
        _sut.TryGet(n2.Id).Should().BeSameAs(n2);
    }

    [Fact]
    public void Store_NullArgument_Throws()
    {
        var act = () => _sut.Store(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
