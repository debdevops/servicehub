using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.Models;

public sealed class ProviderCapabilitiesTests
{
    [Fact]
    public void For_Azure_ReturnsAzurePreset()
    {
        ProviderCapabilities.For(CloudProviderType.Azure).Should().BeSameAs(ProviderCapabilities.Azure);
    }

    [Fact]
    public void For_Aws_ReturnsAwsPreset()
    {
        ProviderCapabilities.For(CloudProviderType.Aws).Should().BeSameAs(ProviderCapabilities.Aws);
    }

    [Fact]
    public void For_Gcp_ReturnsGcpPreset()
    {
        ProviderCapabilities.For(CloudProviderType.Gcp).Should().BeSameAs(ProviderCapabilities.Gcp);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public void AllThreeCurrentProviders_SupportTopicsAndSubscriptions(CloudProviderType provider)
    {
        var capabilities = ProviderCapabilities.For(provider);

        capabilities.SupportsTopics.Should().BeTrue();
        capabilities.SupportsSubscriptions.Should().BeTrue();
    }
}
