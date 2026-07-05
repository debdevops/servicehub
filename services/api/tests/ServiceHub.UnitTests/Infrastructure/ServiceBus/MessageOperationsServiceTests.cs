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
    private static (MessageOperationsService svc, Mock<INamespaceRepository> nsRepo, Mock<ICloudMessagingProvider> providerMock, Mock<IMessageSender> senderMock, Mock<IMessageReceiver> receiverMock, Namespace ns) CreateServiceWithProvider(CloudProviderType providerType)
    {
        var nsId = Guid.NewGuid();
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: providerType);
        nsRes.IsSuccess.Should().BeTrue();
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var senderMock = new Mock<IMessageSender>();
        senderMock.Setup(s => s.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        senderMock.Setup(s => s.SendBatchAsync(It.IsAny<IEnumerable<SendMessageRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var receiverMock = new Mock<IMessageReceiver>();

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(providerType);
        providerMock.Setup(p => p.GetMessageSender()).Returns(senderMock.Object);
        providerMock.Setup(p => p.GetMessageReceiver()).Returns(receiverMock.Object);

        var router = new CloudProviderRouter(new[] { providerMock.Object });

        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        return (svc, nsRepo, providerMock, senderMock, receiverMock, ns);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task SendAsync_ProviderRegistered_DelegatesToSender(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        var req = new SendMessageRequest(ns.Id, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsSuccess.Should().BeTrue();
        senderMock.Verify(s => s.SendAsync(It.Is<SendMessageRequest>(r => r.NamespaceId == ns.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsId = Guid.NewGuid();
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        // Router with no providers -> Resolve will throw
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());

        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new SendMessageRequest(ns.Id, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Should().NotBeNull();
        new[] { ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError }
            .Should().Contain(res.Error.Code);
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

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task PeekMessagesAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        var expected = new List<Message> { new() { MessageId = "m1", SequenceNumber = 1, NamespaceId = ns.Id } };
        receiverMock.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(expected));

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekMessagesAsync(req);

        res.IsSuccess.Should().BeTrue();
        receiverMock.Verify(r => r.PeekMessagesAsync(It.Is<GetMessagesRequest>(q => q.NamespaceId == ns.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PeekMessagesAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsId = Guid.NewGuid();
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task PeekMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new GetMessagesRequest(nsId, "queue");
        var res = await svc.PeekMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task PeekDeadLetterMessagesAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        var expected = new List<Message> { new() { MessageId = "m1", SequenceNumber = 1, NamespaceId = ns.Id } };
        receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(expected));

        var req = new GetMessagesRequest(ns.Id, "queue", null, true);
        var res = await svc.PeekDeadLetterMessagesAsync(req);

        res.IsSuccess.Should().BeTrue();
        receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(It.Is<GetMessagesRequest>(q => q.NamespaceId == ns.Id && q.FromDeadLetter), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task ReplayMessageAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        receiverMock.Setup(r => r.ReplayMessageAsync(ns.Id, "queue", null, 123L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var res = await svc.ReplayMessageAsync(ns.Id, "queue", null, 123L);

        res.IsSuccess.Should().BeTrue();
        receiverMock.Verify(r => r.ReplayMessageAsync(ns.Id, "queue", null, 123L, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task PurgeMessageAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        receiverMock.Setup(r => r.PurgeMessageAsync(ns.Id, "queue", null, 123L, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var res = await svc.PurgeMessageAsync(ns.Id, "queue", null, 123L, false);

        res.IsSuccess.Should().BeTrue();
        receiverMock.Verify(r => r.PurgeMessageAsync(ns.Id, "queue", null, 123L, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task GetMessageCountAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        receiverMock.Setup(r => r.GetMessageCountAsync(ns.Id, "queue", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long>.Success(42));

        var res = await svc.GetMessageCountAsync(ns.Id, "queue", null);

        res.IsSuccess.Should().BeTrue();
        res.Value.Should().Be(42);
        receiverMock.Verify(r => r.GetMessageCountAsync(ns.Id, "queue", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task GetScheduledMessagesAsync_ProviderRegistered_DelegatesToReceiver(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        var expected = new List<Message> { new() { MessageId = "m1", SequenceNumber = 1, NamespaceId = ns.Id } };
        receiverMock.Setup(r => r.GetScheduledMessagesAsync(ns.Id, "queue", null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(expected));

        var res = await svc.GetScheduledMessagesAsync(ns.Id, "queue", null, 10);

        res.IsSuccess.Should().BeTrue();
        receiverMock.Verify(r => r.GetScheduledMessagesAsync(ns.Id, "queue", null, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task SendBatchAsync_ProviderRegistered_DelegatesToSender(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        var requests = new[] { new SendMessageRequest(ns.Id, "queue", "b1") };
        var res = await svc.SendBatchAsync(requests);

        res.IsSuccess.Should().BeTrue();
        senderMock.Verify(s => s.SendBatchAsync(It.IsAny<IEnumerable<SendMessageRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Validation_NamespaceMissing_ReturnsValidationFailure()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        var req = new SendMessageRequest(null, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task SendBatchAsync_EmptyRequests_ReturnsSuccess()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        var res = await svc.SendBatchAsync(Array.Empty<SendMessageRequest>());

        res.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_FirstNamespaceMissing_ReturnsValidationFailure()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        var requests = new[] { new SendMessageRequest(null, "queue", "b1") };
        var res = await svc.SendBatchAsync(requests);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(CloudProviderType.Azure)]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task SendAsync_SenderThrows_ReturnsUnexpectedError(CloudProviderType providerType)
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(providerType);

        senderMock.Setup(s => s.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var req = new SendMessageRequest(ns.Id, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task PeekMessages_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task GetMessageCount_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.GetMessageCountAsync(ns.Id, "queue", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var res = await svc.GetMessageCountAsync(ns.Id, "queue", null);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new GetMessagesRequest(nsId, "queue");
        var res = await svc.PeekDeadLetterMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekDeadLetterMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekDeadLetterMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task ReplayMessageAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.ReplayMessageAsync(nsId, "queue", null, 123L);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task ReplayMessageAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.ReplayMessageAsync(ns.Id, "queue", null, 123L);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task ReplayMessageAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.ReplayMessageAsync(ns.Id, "queue", null, 123L, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var res = await svc.ReplayMessageAsync(ns.Id, "queue", null, 123L);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task PurgeMessageAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.PurgeMessageAsync(nsId, "queue", null, 123L, false);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task PurgeMessageAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.PurgeMessageAsync(ns.Id, "queue", null, 123L, false);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task PurgeMessageAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.PurgeMessageAsync(ns.Id, "queue", null, 123L, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var res = await svc.PurgeMessageAsync(ns.Id, "queue", null, 123L, false);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task GetMessageCountAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.GetMessageCountAsync(nsId, "queue", null);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task GetMessageCountAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.GetMessageCountAsync(ns.Id, "queue", null);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task GetScheduledMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.GetScheduledMessagesAsync(nsId, "queue", null, 10);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task GetScheduledMessagesAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var res = await svc.GetScheduledMessagesAsync(ns.Id, "queue", null, 10);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task GetScheduledMessagesAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.GetScheduledMessagesAsync(ns.Id, "queue", null, 10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var res = await svc.GetScheduledMessagesAsync(ns.Id, "queue", null, 10);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_Success_DelegatesToReceiver()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.DeadLetterMessagesAsync(It.IsAny<DeadLetterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(5));

        var req = new DeadLetterRequest(ns.Id, "queue", null, 1, "ManualDeadLetter");
        var res = await svc.DeadLetterMessagesAsync(req);

        res.IsSuccess.Should().BeTrue();
        res.Value.Should().Be(5);
        receiverMock.Verify(r => r.DeadLetterMessagesAsync(It.IsAny<DeadLetterRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var req = new DeadLetterRequest(nsId, "queue", null, 1, "ManualDeadLetter");
        var res = await svc.DeadLetterMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.DeadLetterMessagesAsync(It.IsAny<DeadLetterRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var req = new DeadLetterRequest(ns.Id, "queue", null, 1, "ManualDeadLetter");
        var res = await svc.DeadLetterMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task SendBatchAsync_ProviderNotRegistered_ReturnsFailure()
    {
        var nsRes = Namespace.CreateWithManagedIdentity("test", provider: CloudProviderType.Azure);
        var ns = nsRes.Value;

        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var requests = new[] { new SendMessageRequest(ns.Id, "queue", "b1") };
        var res = await svc.SendBatchAsync(requests);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task SendBatchAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        var nsRepo = new Mock<INamespaceRepository>();
        nsRepo.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(ServiceHub.Shared.Results.Error.NotFound(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound, "not found")));

        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var svc = new MessageOperationsService(router, nsRepo.Object, NullLogger<MessageOperationsService>.Instance);

        var requests = new[] { new SendMessageRequest(nsId, "queue", "b1") };
        var res = await svc.SendBatchAsync(requests);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task SendBatchAsync_SenderThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        senderMock.Setup(s => s.SendBatchAsync(It.IsAny<IEnumerable<SendMessageRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var requests = new[] { new SendMessageRequest(ns.Id, "queue", "b1") };
        var res = await svc.SendBatchAsync(requests);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task PeekMessagesAsync_ReceiverThrows_ReturnsUnexpectedError()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        receiverMock.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var req = new GetMessagesRequest(ns.Id, "queue");
        var res = await svc.PeekMessagesAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ServiceHub.Shared.Constants.ErrorCodes.General.UnexpectedError);
    }

    [Fact]
    public async Task SendAsync_SenderReturnsFailure_ReturnsFailure()
    {
        var (svc, nsRepo, providerMock, senderMock, receiverMock, ns) = CreateServiceWithProvider(CloudProviderType.Azure);

        senderMock.Setup(s => s.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ServiceHub.Shared.Results.Error.Internal("Send.Failed", "Failed to send")));

        var req = new SendMessageRequest(ns.Id, "queue", "body");
        var res = await svc.SendAsync(req);

        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be("Send.Failed");
    }
}
