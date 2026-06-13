using FluentAssertions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.UnitTests.Infrastructure;

/// <summary>
/// Direct unit tests for <see cref="CloudProviderRouter"/>.
/// These are distinct from the CloudBridgeController tests (which test the HTTP layer).
/// </summary>
public sealed class CloudProviderRouterUnitTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullProviders_Throws()
    {
        var act = () => new CloudProviderRouter(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("providers");
    }

    [Fact]
    public void Constructor_EmptyProviders_DoesNotThrow()
    {
        var act = () => new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithProviders_DoesNotThrow()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var act = () => new CloudProviderRouter(new[] { awsProvider.Object });
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolve — success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_WhenAwsProviderRegistered_ReturnsAwsProvider()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var router = new CloudProviderRouter(new[] { awsProvider.Object });

        var result = router.Resolve(CloudProviderType.Aws);

        result.Should().Be(awsProvider.Object);
    }

    [Fact]
    public void Resolve_WhenGcpProviderRegistered_ReturnsGcpProvider()
    {
        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Gcp);

        var router = new CloudProviderRouter(new[] { gcpProvider.Object });

        var result = router.Resolve(CloudProviderType.Gcp);

        result.Should().Be(gcpProvider.Object);
    }

    [Fact]
    public void Resolve_WithMultipleProviders_ReturnsCorrectProvider()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Gcp);

        var azureProvider = new Mock<ICloudMessagingProvider>();
        azureProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Azure);

        var router = new CloudProviderRouter(new[] { awsProvider.Object, gcpProvider.Object, azureProvider.Object });

        router.Resolve(CloudProviderType.Aws).Should().Be(awsProvider.Object);
        router.Resolve(CloudProviderType.Gcp).Should().Be(gcpProvider.Object);
        router.Resolve(CloudProviderType.Azure).Should().Be(azureProvider.Object);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolve — failure (provider not registered)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_WhenProviderNotRegistered_ThrowsInvalidOperationException()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        var act = () => router.Resolve(CloudProviderType.Aws);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No ICloudMessagingProvider has been registered for cloud provider 'Aws'*");
    }

    [Fact]
    public void Resolve_WhenGcpNotRegistered_ThrowsWithDiagnosticMessage()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var router = new CloudProviderRouter(new[] { awsProvider.Object });

        // Only AWS is registered — resolving GCP should throw
        var act = () => router.Resolve(CloudProviderType.Gcp);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Gcp*")
            .WithMessage("*Registered providers*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsRegistered
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegistered_WhenProviderRegistered_ReturnsTrue()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var router = new CloudProviderRouter(new[] { awsProvider.Object });

        router.IsRegistered(CloudProviderType.Aws).Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_WhenProviderNotRegistered_ReturnsFalse()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var router = new CloudProviderRouter(new[] { awsProvider.Object });

        router.IsRegistered(CloudProviderType.Gcp).Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_WhenNoProvidersRegistered_ReturnsFalseForAll()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        router.IsRegistered(CloudProviderType.Aws).Should().BeFalse();
        router.IsRegistered(CloudProviderType.Gcp).Should().BeFalse();
        router.IsRegistered(CloudProviderType.Azure).Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_DoesNotThrow_WhenProviderNotPresent()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        var act = () => router.IsRegistered(CloudProviderType.Gcp);
        act.Should().NotThrow();
    }
}
