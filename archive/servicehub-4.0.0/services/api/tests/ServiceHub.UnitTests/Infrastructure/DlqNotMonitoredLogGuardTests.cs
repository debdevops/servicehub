using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class DlqNotMonitoredLogGuardTests
{
    [Fact]
    public void ShouldLog_FirstCallForProvider_ReturnsTrue()
    {
        var guard = new DlqNotMonitoredLogGuard();

        guard.ShouldLog(CloudProviderType.Aws).Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_SecondCallForSameProvider_ReturnsFalse()
    {
        var guard = new DlqNotMonitoredLogGuard();

        guard.ShouldLog(CloudProviderType.Aws);
        guard.ShouldLog(CloudProviderType.Aws).Should().BeFalse();
    }

    [Fact]
    public void ShouldLog_DifferentProviders_AreTrackedIndependently()
    {
        var guard = new DlqNotMonitoredLogGuard();

        guard.ShouldLog(CloudProviderType.Aws).Should().BeTrue();
        guard.ShouldLog(CloudProviderType.Gcp).Should().BeTrue();
    }
}
