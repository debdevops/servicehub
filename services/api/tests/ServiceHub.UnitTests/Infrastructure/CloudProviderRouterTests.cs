using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class CloudProviderRouterTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static CloudBridgeController BuildController(params ICloudMessagingProvider[] providers)
    {
        var ctrl = new CloudBridgeController(providers, NullLogger<CloudBridgeController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }

    // -------------------------------------------------------------------------
    // GetProviderStatus
    // -------------------------------------------------------------------------

    [Fact]
    public void GetProviderStatus_WithNoProviders_ReturnsBothFalse()
    {
        var ctrl = BuildController();

        var result = ctrl.GetProviderStatus() as OkObjectResult;

        result.Should().NotBeNull();
        var dict = result!.Value as Dictionary<string, bool>;
        dict.Should().NotBeNull();
        dict![CloudProviderType.Aws.ToString()].Should().BeFalse();
        dict[CloudProviderType.Gcp.ToString()].Should().BeFalse();
    }

    [Fact]
    public void GetProviderStatus_WithAwsProvider_ReturnsAwsTrue()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var ctrl = BuildController(awsProvider.Object);

        var result = ctrl.GetProviderStatus() as OkObjectResult;
        var dict = result!.Value as Dictionary<string, bool>;

        dict![CloudProviderType.Aws.ToString()].Should().BeTrue();
        dict[CloudProviderType.Gcp.ToString()].Should().BeFalse();
    }

    [Fact]
    public void GetProviderStatus_WithBothProviders_ReturnsBothTrue()
    {
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Gcp);

        var ctrl = BuildController(awsProvider.Object, gcpProvider.Object);

        var result = ctrl.GetProviderStatus() as OkObjectResult;
        var dict = result!.Value as Dictionary<string, bool>;

        dict![CloudProviderType.Aws.ToString()].Should().BeTrue();
        dict[CloudProviderType.Gcp.ToString()].Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // ListEntities
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListEntities_WithInvalidProviderString_Returns400()
    {
        var ctrl = BuildController();

        var result = await ctrl.ListEntities(TestNamespaceId, "NotARealProvider", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListEntities_WithAzureProvider_Returns404_NotRegisteredInBridge()
    {
        var ctrl = BuildController(); // no providers registered

        var result = await ctrl.ListEntities(TestNamespaceId, "Azure", CancellationToken.None);

        // Azure is a valid enum but no provider is registered in the cloud bridge
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListEntities_WithUnregisteredProvider_Returns404()
    {
        var ctrl = BuildController(); // no AWS provider registered

        var result = await ctrl.ListEntities(TestNamespaceId, "Aws", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListEntities_WhenProviderSucceeds_Returns200WithEntities()
    {
        var entities = new List<CloudEntity>
        {
            new CloudEntity { Name = "queue-1", EntityType = "Queue", Provider = CloudProviderType.Aws }
        };

        var provider = new Mock<ICloudMessagingProvider>();
        provider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);
        provider.Setup(p => p.ListEntitiesAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CloudEntity>>(entities));

        var ctrl = BuildController(provider.Object);

        var result = await ctrl.ListEntities(TestNamespaceId, "Aws", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(entities);
    }

    [Fact]
    public async Task ListEntities_WhenProviderFails_Returns502()
    {
        var provider = new Mock<ICloudMessagingProvider>();
        provider.Setup(p => p.ProviderType).Returns(CloudProviderType.Aws);
        provider.Setup(p => p.ListEntitiesAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<CloudEntity>>(
                Error.ExternalService("AWS.SQS.ListFailed", "Connection refused")));

        var ctrl = BuildController(provider.Object);

        var result = await ctrl.ListEntities(TestNamespaceId, "Aws", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }
}
