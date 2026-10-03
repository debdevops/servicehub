using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Security;

namespace ServiceHub.UnitTests.Api.Security;

public class AuthFailureThrottleTests
{
    private static AuthFailureThrottle CreateThrottle(int threshold = 10, TimeSpan? window = null)
    {
        var options = new AuthFailureThrottleOptions
        {
            Threshold = threshold,
            Window = window ?? TimeSpan.FromMinutes(5)
        };
        return new AuthFailureThrottle(Options.Create(options));
    }

    [Fact]
    public void IsLockedOut_UnderThreshold_ReturnsFalse()
    {
        var throttle = CreateThrottle(threshold: 10);

        for (var i = 0; i < 9; i++)
        {
            throttle.RecordFailure("1.2.3.4");
        }

        throttle.IsLockedOut("1.2.3.4", out _).Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_AtThreshold_ReturnsTrue()
    {
        var throttle = CreateThrottle(threshold: 10);

        for (var i = 0; i < 10; i++)
        {
            throttle.RecordFailure("1.2.3.4");
        }

        throttle.IsLockedOut("1.2.3.4", out var retryAfter).Should().BeTrue();
        retryAfter.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void IsLockedOut_AfterWindowExpires_ResetsAndReturnsFalse()
    {
        var throttle = CreateThrottle(threshold: 3, window: TimeSpan.FromMilliseconds(50));

        for (var i = 0; i < 3; i++)
        {
            throttle.RecordFailure("1.2.3.4");
        }

        throttle.IsLockedOut("1.2.3.4", out _).Should().BeTrue();

        Thread.Sleep(100);

        throttle.IsLockedOut("1.2.3.4", out _).Should().BeFalse();
    }

    [Fact]
    public void RecordSuccess_ClearsFailureCount()
    {
        var throttle = CreateThrottle(threshold: 3);

        throttle.RecordFailure("1.2.3.4");
        throttle.RecordFailure("1.2.3.4");
        throttle.RecordSuccess("1.2.3.4");
        throttle.RecordFailure("1.2.3.4");

        // Only one failure since the successful reset — nowhere near the threshold of 3.
        throttle.IsLockedOut("1.2.3.4", out _).Should().BeFalse();
    }

    [Fact]
    public void RecordFailure_DifferentClients_AreIndependentlyTracked()
    {
        var throttle = CreateThrottle(threshold: 2);

        throttle.RecordFailure("1.2.3.4");
        throttle.RecordFailure("1.2.3.4");
        throttle.RecordFailure("5.6.7.8");

        throttle.IsLockedOut("1.2.3.4", out _).Should().BeTrue();
        throttle.IsLockedOut("5.6.7.8", out _).Should().BeFalse();
    }

    [Fact]
    public void RecordFailure_BeyondCapacity_EvictsOldestEntriesInsteadOfGrowingUnbounded()
    {
        // Threshold of 1 means a client is locked out the instant its own failure is recorded —
        // isolating eviction as the only thing that could make the earliest client's lockout
        // disappear by the end of the loop.
        var throttle = CreateThrottle(threshold: 1);

        // MaxTrackedClients is 10_000 internally; push well past it to force eviction.
        for (var i = 0; i < 10_050; i++)
        {
            throttle.RecordFailure($"10.0.{i / 256}.{i % 256}");
        }

        // The earliest-recorded client should have been evicted to make room, so it now reads
        // as never having failed — proving the dictionary stayed bounded rather than growing forever.
        throttle.IsLockedOut("10.0.0.0", out _).Should().BeFalse();

        // A recently-recorded client must still be tracked and locked out.
        throttle.IsLockedOut("10.0.39.65", out _).Should().BeTrue();
    }
}
