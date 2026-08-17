using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class CloudBridgeControllerTests
{
    private readonly Mock<ILogger<CloudBridgeController>> _loggerMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly List<ICloudMessagingProvider> _providers = new();
    private readonly CloudBridgeController _controller;

    public CloudBridgeControllerTests()
    {
        // Default: any namespace lookup succeeds and belongs to the SPA owner
        // (the OwnerId ApiControllerBase falls back to when none is set).
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => Result<Namespace>.Success(CreateNamespace(Namespace.SpaOwnerId)));

        _controller = new CloudBridgeController(_providers, _namespaceRepositoryMock.Object, _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static Namespace CreateNamespace(string ownerId)
    {
        return Namespace.Create(
            "aws-queue",
            "https://sqs.us-east-1.amazonaws.com/123456789012/my-queue",
            provider: CloudProviderType.Aws,
            ownerId: ownerId).Value;
    }

    [Fact]
    public void GetProviderStatus_ReturnsCorrectStatus()
    {
        // Arrange
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        _providers.Add(awsProvider.Object);

        // Act
        var result = _controller.GetProviderStatus();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var status = (Dictionary<string, bool>)okResult.Value!;
        status.Should().ContainKey("Azure");
        status.Should().ContainKey("Aws").WhoseValue.Should().BeTrue();
        status.Should().ContainKey("Gcp").WhoseValue.Should().BeFalse();
    }

    [Fact]
    public void GetCapabilities_ReturnsAllThreeProviders_RegardlessOfRegistration()
    {
        // Act — no providers registered in _providers at all.
        var result = _controller.GetCapabilities();

        // Assert — capabilities are provider-type-inherent facts, not tied to a live
        // registration, so all three known providers are returned unconditionally.
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var capabilities = (Dictionary<string, ProviderCapabilitiesResponse>)okResult.Value!;

        capabilities.Should().ContainKey("Azure");
        capabilities.Should().ContainKey("Aws");
        capabilities.Should().ContainKey("Gcp");

        capabilities["Azure"].SupportsPurge.Should().BeFalse();
        capabilities["Aws"].SupportsPurge.Should().BeTrue();
        capabilities["Gcp"].SupportsMessageCounts.Should().BeFalse();
        capabilities["Gcp"].SupportsManualDeadLetter.Should().BeFalse();

        capabilities["Azure"].SupportsRepeatablePeek.Should().BeTrue();
        capabilities["Aws"].SupportsRepeatablePeek.Should().BeFalse();
        capabilities["Gcp"].SupportsRepeatablePeek.Should().BeTrue();

        // CanProveDlqAbsence is the fact that determines whether a provider can ever earn L4/L5
        // auto-replay — must be surfaced to the frontend, not left backend-only.
        capabilities["Azure"].CanProveDlqAbsence.Should().BeTrue();
        capabilities["Aws"].CanProveDlqAbsence.Should().BeFalse();
        capabilities["Gcp"].CanProveDlqAbsence.Should().BeFalse();

        capabilities["Azure"].SupportsRecoveryMarker.Should().BeTrue();
        capabilities["Aws"].SupportsRecoveryMarker.Should().BeTrue();
        capabilities["Gcp"].SupportsRecoveryMarker.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListEntities_NullOrEmptyProvider_ReturnsBadRequest(string? provider)
    {
        // Act
        var result = await _controller.ListEntities(Guid.NewGuid(), provider!, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListEntities_InvalidProvider_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.ListEntities(Guid.NewGuid(), "invalid-provider", CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListEntities_UnregisteredProvider_ReturnsServiceUnavailable()
    {
        // Act
        var result = await _controller.ListEntities(Guid.NewGuid(), "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task ListEntities_ProviderFailure_ReturnsBadGateway()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider
            .Setup(p => p.ListEntitiesAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Failure(Error.ExternalService("AWS.SQS.Error", "List failed.")));
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.ListEntities(nsId, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task ListEntities_ProviderReturnsNotFound_ReturnsNotFound()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider
            .Setup(p => p.ListEntitiesAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Failure(Error.NotFound("NotFound.Queue", "Queue not found.")));
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.ListEntities(nsId, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListEntities_Success_ReturnsOk()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        var entities = new List<CloudEntity>
        {
            new CloudEntity { Name = "my-queue", EntityType = "Queue", Provider = CloudProviderType.Aws }
        };
        awsProvider
            .Setup(p => p.ListEntitiesAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.ListEntities(nsId, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var returned = (IReadOnlyList<CloudEntity>)okResult.Value!;
        returned.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListEntities_NamespaceOwnedByAnotherTenant_ReturnsNotFound()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        _providers.Add(awsProvider.Object);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateNamespace("key_othertenant")));

        // Act
        var result = await _controller.ListEntities(nsId, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        awsProvider.Verify(
            p => p.ListEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListEntities_NamespaceNotFound_ReturnsNotFound()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        _providers.Add(awsProvider.Object);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "Not found.")));

        // Act
        var result = await _controller.ListEntities(nsId, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetVisibilityStatus_NamespaceOwnedByAnotherTenant_ReturnsNotFound()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        _providers.Add(awsProvider.Object);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateNamespace("key_othertenant")));

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, "my-queue", "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        awsProvider.Verify(p => p.GetMessageReceiver(), Times.Never);
    }

    [Fact]
    public async Task GetVisibilityStatus_AwsSuccess_ReturnsOk()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var queueName = "my-queue";
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var awsReceiverMock = new Mock<IMessageReceiver>();
        var visibilityMock = awsReceiverMock.As<IVisibilityStatusProvider>();
        var visibilityInfo = new SqsVisibilityInfo(InFlightCount: 1, VisibilityTimeoutSeconds: 30, DlqCount: 2);
        visibilityMock
            .Setup(r => r.GetVisibilityWindowStatusAsync(nsId, queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SqsVisibilityInfo>.Success(visibilityInfo));

        awsProvider.Setup(p => p.GetMessageReceiver()).Returns(awsReceiverMock.Object);
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, queueName, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var returned = (SqsVisibilityInfo)okResult.Value!;
        returned.InFlightCount.Should().Be(1);
    }

    [Fact]
    public async Task GetVisibilityStatus_AwsReceiverUnavailable_ReturnsBadGateway()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var queueName = "my-queue";
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider.Setup(p => p.GetMessageReceiver()).Returns(new Mock<IMessageReceiver>().Object); // doesn't implement IVisibilityStatusProvider
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, queueName, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task GetVisibilityStatus_AwsFailure_ReturnsBadGateway()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var queueName = "my-queue";
        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);

        var awsReceiverMock = new Mock<IMessageReceiver>();
        var visibilityMock = awsReceiverMock.As<IVisibilityStatusProvider>();
        visibilityMock
            .Setup(r => r.GetVisibilityWindowStatusAsync(nsId, queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SqsVisibilityInfo>.Failure(Error.ExternalService("SQS.Error", "Error")));

        awsProvider.Setup(p => p.GetMessageReceiver()).Returns(awsReceiverMock.Object);
        _providers.Add(awsProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, queueName, "Aws", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task GetVisibilityStatus_GcpSuccess_ReturnsOk()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var subName = "my-sub";
        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);

        var gcpReceiverMock = new Mock<IMessageReceiver>();
        var ackMock = gcpReceiverMock.As<IAckDeadlineStatusProvider>();
        var ackStatus = new GcpAckDeadlineStatus(AckDeadlineSeconds: 30, HasDeadLetterPolicy: true, DeadLetterTopic: "dlq", MaxDeliveryAttempts: 5, MessageOrderingEnabled: true);
        ackMock
            .Setup(r => r.GetAckDeadlineStatusAsync(nsId, subName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GcpAckDeadlineStatus>.Success(ackStatus));

        gcpProvider.Setup(p => p.GetMessageReceiver()).Returns(gcpReceiverMock.Object);
        _providers.Add(gcpProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, subName, "Gcp", CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var returned = (GcpAckDeadlineStatus)okResult.Value!;
        returned.AckDeadlineSeconds.Should().Be(30);
    }

    [Fact]
    public async Task GetVisibilityStatus_GcpReceiverUnavailable_ReturnsBadGateway()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var subName = "my-sub";
        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);
        gcpProvider.Setup(p => p.GetMessageReceiver()).Returns(new Mock<IMessageReceiver>().Object); // doesn't implement IAckDeadlineStatusProvider
        _providers.Add(gcpProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, subName, "Gcp", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task GetVisibilityStatus_GcpFailure_ReturnsBadGateway()
    {
        // Arrange
        var nsId = Guid.NewGuid();
        var subName = "my-sub";
        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);

        var gcpReceiverMock = new Mock<IMessageReceiver>();
        var ackMock = gcpReceiverMock.As<IAckDeadlineStatusProvider>();
        ackMock
            .Setup(r => r.GetAckDeadlineStatusAsync(nsId, subName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GcpAckDeadlineStatus>.Failure(Error.ExternalService("PubSub.Error", "Error")));

        gcpProvider.Setup(p => p.GetMessageReceiver()).Returns(gcpReceiverMock.Object);
        _providers.Add(gcpProvider.Object);

        // Act
        var result = await _controller.GetVisibilityStatus(nsId, subName, "Gcp", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(502);
    }
}
