using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Results;
using Xunit;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

public class MessageOperationsServiceTests
{
    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task SendAsync_ProviderRegistered_DelegatesToSender(CloudProviderType providerType)
    {
        var nsId = Guid.NewGuid();
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: providerType);
        nsRes.IsSuccess.Should().BeTrue();
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var senderMock = new Mock<IMessageSender>();
        senderMock.Setup(s => s.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(providerType);
        providerMock.Setup(p => p.GetMessageSender()).Returns(senderMock.Object);

        var router = new CloudProviderRouter(new[] { providerMock.Object });

        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new SendMessageRequest(nsId, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsSuccess.Should().BeTrue();
        senderMock.Verify(s => s.SendAsync(It.Is<SendMessageRequest>(r => r.NamespaceId == nsId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsId = Guid.NewGuid();
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        // Router with no providers -> Resolve will throw
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new SendMessageRequest(nsId, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task SendAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new SendMessageRequest(nsId, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }
}
