using FluentAssertions;
using ServiceHub.Infrastructure.LiveTail;

namespace ServiceHub.UnitTests.Infrastructure.LiveTail;

public sealed class LiveTailConnectionLimiterTests
{
    [Fact]
    public void TryAcquire_UnderCap_ReturnsTrue()
    {
        var sut = new LiveTailConnectionLimiter();

        sut.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_AtCap_ReturnsFalse()
    {
        var sut = new LiveTailConnectionLimiter();

        for (var i = 0; i < 20; i++)
        {
            sut.TryAcquire().Should().BeTrue();
        }

        sut.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public void Release_FreesASlot_ForASubsequentAcquire()
    {
        var sut = new LiveTailConnectionLimiter();

        for (var i = 0; i < 20; i++)
        {
            sut.TryAcquire().Should().BeTrue();
        }

        sut.TryAcquire().Should().BeFalse();

        sut.Release();

        sut.TryAcquire().Should().BeTrue();
    }
}
