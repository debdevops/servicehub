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
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class SubscriptionsControllerTests
{
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<IServiceBusClientCache> _clientCache;
    private readonly Mock<IConnectionStringProtector> _connectionStringProtector;
    private readonly Mock<ILogger<SubscriptionsController>> _logger;
    private readonly SubscriptionsController _controller;

    public SubscriptionsControllerTests()
    {
        _namespaceRepository = new Mock<INamespaceRepository>();
        _clientCache = new Mock<IServiceBusClientCache>();
        _connectionStringProtector = new Mock<IConnectionStringProtector>();
        _logger = new Mock<ILogger<SubscriptionsController>>();

        _controller = new SubscriptionsController(
            _namespaceRepository.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static Namespace CreateTestNamespace()
    {
        var result = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS");
        return result.Value;
    }

    private static SubscriptionRuntimePropertiesDto CreateTestSubscription(string name = "test-sub", string topicName = "test-topic")
    {
        return new SubscriptionRuntimePropertiesDto(
            Name: name,
            TopicName: topicName,
            ActiveMessageCount: 5,
            DeadLetterMessageCount: 1,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: true,
            EnableDeadLetteringOnFilterEvaluationExceptions: false,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: null,
            ForwardDeadLetteredMessagesTo: null);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullRepository_ShouldThrow()
    {
        var act = () => new SubscriptionsController(
            null!, _clientCache.Object, _connectionStringProtector.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new SubscriptionsController(
            _namespaceRepository.Object, _clientCache.Object, _connectionStringProtector.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public async Task GetAll_Success_ShouldReturnOkWithSubscriptions()
    {
        var ns = CreateTestNamespace();
        var subs = new List<SubscriptionRuntimePropertiesDto>
        {
            CreateTestSubscription("sub1"),
            CreateTestSubscription("sub2")
        };

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetSubscriptionsAsync("test-topic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(subs));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        var result = await _controller.GetAll(ns.Id, "test-topic");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedSubs = okResult.Value.Should().BeAssignableTo<IReadOnlyList<SubscriptionRuntimePropertiesDto>>().Subject;
        returnedSubs.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetAll(id, "test-topic");

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetAll_NoConnectionString_ShouldReturnBadRequest()
    {
        var nsResult = Namespace.CreateWithManagedIdentity("test-mi", ConnectionAuthType.ManagedIdentity);
        var ns = nsResult.Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.GetAll(ns.Id, "test-topic");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetAll_UnprotectFails_ShouldReturnError()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Failure(Error.Internal("DECRYPT_ERR", "Failed")));

        var result = await _controller.GetAll(ns.Id, "test-topic");

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    #endregion

    #region GetByName Tests

    [Fact]
    public async Task GetByName_Success_ShouldReturnOkWithSubscription()
    {
        var ns = CreateTestNamespace();
        var sub = CreateTestSubscription("my-sub", "my-topic");

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetSubscriptionAsync("my-topic", "my-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionRuntimePropertiesDto>.Success(sub));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        var result = await _controller.GetByName(ns.Id, "my-topic", "my-sub");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedSub = okResult.Value.Should().BeOfType<SubscriptionRuntimePropertiesDto>().Subject;
        returnedSub.Name.Should().Be("my-sub");
    }

    [Fact]
    public async Task GetByName_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetByName(id, "topic", "sub");

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
