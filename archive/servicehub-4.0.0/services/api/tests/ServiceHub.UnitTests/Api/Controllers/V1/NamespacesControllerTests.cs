using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class NamespacesControllerTests
{
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<IServiceBusClientFactory> _clientFactory;
    private readonly Mock<IServiceBusClientCache> _clientCache;
    private readonly Mock<IConnectionStringProtector> _connectionStringProtector;
    private readonly Mock<ILogger<NamespacesController>> _logger;
    private readonly Mock<IPlatformEventBus> _eventBus;
    private readonly Mock<IAuditLogger> _auditLogger;
    private readonly NamespacesController _controller;

    public NamespacesControllerTests()
    {
        _namespaceRepository = new Mock<INamespaceRepository>();
        _clientFactory = new Mock<IServiceBusClientFactory>();
        _clientCache = new Mock<IServiceBusClientCache>();
        _connectionStringProtector = new Mock<IConnectionStringProtector>();
        _logger = new Mock<ILogger<NamespacesController>>();
        _eventBus = new Mock<IPlatformEventBus>();
        _auditLogger = new Mock<IAuditLogger>();

        // Default: PublishAsync is a no-op ValueTask — does not throw.
        _eventBus
            .Setup(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _controller = new NamespacesController(
            _namespaceRepository.Object,
            _clientFactory.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object,
            auditLogger: _auditLogger.Object,
            eventBus: _eventBus.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider()
                }
            }
        };

        // Provide a valid ApiKeyConfig so in-method scope checks pass
        _controller.ControllerContext.HttpContext.Items["ApiKeyConfig"] = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = null  // null = admin (all scopes granted)
        };
    }

    private static Namespace CreateTestNamespace(string name = "test-namespace", string connectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=")
    {
        var result = Namespace.Create(name, connectionString, "Test NS", "Test Description");
        return result.Value;
    }

    private void SetIntentHeaders(string intent)
    {
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.IntentHeaderName] = intent;
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.ConfirmHeaderName] = "true";
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullRepository_ShouldThrow()
    {
        var act = () => new NamespacesController(
            null!,
            _clientFactory.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullClientFactory_ShouldThrow()
    {
        var act = () => new NamespacesController(
            _namespaceRepository.Object,
            null!,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new NamespacesController(
            _namespaceRepository.Object,
            _clientFactory.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public async Task GetAll_Success_ShouldReturnOkWithNamespaces()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var result = await _controller.GetAll();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responses = okResult.Value.Should().BeAssignableTo<List<NamespaceResponse>>().Subject;
        responses.Should().HaveCount(1);
        responses[0].Name.Should().Be(ns.Name);
    }

    [Fact]
    public async Task GetAll_Failure_ShouldReturnError()
    {
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("ERR", "Failed")));

        var result = await _controller.GetAll();

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_Success_ShouldReturnOkWithNamespace()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.GetById(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceResponse>().Subject;
        response.Id.Should().Be(ns.Id);
        response.Name.Should().Be(ns.Name);
        response.DisplayName.Should().Be(ns.DisplayName);
        response.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        response.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound(ErrorCodes.Namespace.NotFound, "Not found")));

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_Success_ShouldReturnCreated()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ConnectionAuthType.ConnectionString,
            "Test NS");

        // Controller now uses GetByOwnerAsync (owner-scoped) for all duplicate detection.
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _clientFactory.Setup(f => f.ValidateConnectionString(It.IsAny<string>()))
            .Returns(Result.Success());

        _connectionStringProtector.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns(Result<string>.Success("PROTECTED:encrypted-conn-string-data"));

        _namespaceRepository.Setup(r => r.AddAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Namespace.Create", "Succeeded",
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), "test-namespace", It.IsAny<long?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateName_ShouldReturnConflict()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ConnectionAuthType.ConnectionString);

        var existingNs = CreateTestNamespace(); // OwnerId = "__spa__", same as controller default
        // Return a list containing a namespace with the same name as the request.
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { existingNs }));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<ConflictObjectResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Namespace.Create", It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Create_MissingConnectionString_ShouldReturnBadRequest()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            null,
            ConnectionAuthType.ConnectionString);

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_InvalidConnectionString_ShouldReturnError()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            "invalid-conn-string",
            ConnectionAuthType.ConnectionString);

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _clientFactory.Setup(f => f.ValidateConnectionString(It.IsAny<string>()))
            .Returns(Result.Failure(Error.Validation("INVALID", "Invalid connection string")));

        var result = await _controller.Create(request);

        result.Result.Should().NotBeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Create_ManagedIdentity_ShouldReturnCreated()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            null,
            ConnectionAuthType.ManagedIdentity,
            "Test MI NS");

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _namespaceRepository.Setup(r => r.AddAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    #endregion

    #region TestConnection Tests

    [Fact]
    public async Task TestConnection_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound(ErrorCodes.Namespace.NotFound, "Not found")));

        var result = await _controller.TestConnection(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task TestConnection_NoConnectionString_ShouldReturnNotConnected()
    {
        var nsResult = Namespace.CreateWithManagedIdentity("test-managed-id", ConnectionAuthType.ManagedIdentity, "Test MI");
        var ns = nsResult.Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_Success_ShouldReturnConnected()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("unprotected-conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new List<QueueRuntimePropertiesDto>()));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        _namespaceRepository.Setup(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnection_ConnectionFails_ShouldReturnNotConnected()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("unprotected-conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Failure(Error.ExternalService("SB_ERR", "Connection failed")));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        _namespaceRepository.Setup(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_UnprotectFails_ShouldReturnNotConnected()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Failure(Error.Internal("DECRYPT_ERR", "Failed to decrypt")));

        var result = await _controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_Exception_ShouldReturnNotConnected()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("unprotected-conn-string"));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Throws(new InvalidOperationException("Connection error"));

        _namespaceRepository.Setup(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_AwsSuccess_ShouldRouteToProviderAndReturnConnected()
    {
        var ns = Namespace.Create("aws-ns", "AKIAFAKE:secretkey", "AWS Test", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        _namespaceRepository.Setup(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider.Setup(p => p.ValidateConnectionAsync(ns, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var controller = CreateController(messagingProviders: [awsProvider.Object]);

        var result = await controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeTrue();
        awsProvider.Verify(p => p.ValidateConnectionAsync(ns, It.IsAny<CancellationToken>()), Times.Once);
        _clientCache.Verify(c => c.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TestConnection_GcpFailure_ShouldReturnNotConnectedWithProviderError()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", "GCP Test", provider: CloudProviderType.Gcp, gcpProjectId: "my-project").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        _namespaceRepository.Setup(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);
        gcpProvider.Setup(p => p.ValidateConnectionAsync(ns, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Validation("GCP.PubSub.AuthFailed", "Invalid service account")));

        var controller = CreateController(messagingProviders: [gcpProvider.Object]);

        var result = await controller.TestConnection(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConnectionTestResponse>().Subject;
        response.IsConnected.Should().BeFalse();
        response.Message.Should().Contain("Invalid service account");
    }

    [Fact]
    public async Task TestConnection_AwsProviderNotRegistered_ShouldReturnServiceUnavailable()
    {
        var ns = Namespace.Create("aws-ns", "AKIAFAKE:secretkey", "AWS Test", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var controller = CreateController(messagingProviders: []);

        var result = await controller.TestConnection(ns.Id);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _namespaceRepository.Verify(r => r.UpdateAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private NamespacesController CreateController(IEnumerable<ICloudMessagingProvider> messagingProviders)
    {
        var controller = new NamespacesController(
            _namespaceRepository.Object,
            _clientFactory.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object,
            auditLogger: null,
            eventBus: _eventBus.Object,
            messagingProviders: messagingProviders)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider()
                }
            }
        };

        controller.ControllerContext.HttpContext.Items["ApiKeyConfig"] = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = null
        };

        return controller;
    }

    #endregion

    #region GetStats Tests (AWS/GCP)

    [Fact]
    public async Task GetStats_AwsSuccess_ShouldAggregateFromListEntities()
    {
        var ns = Namespace.Create("aws-ns", "AKIAFAKE:secretkey", "AWS Test", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var entities = new List<CloudEntity>
        {
            new() { Name = "orders", EntityType = "Queue", ActiveMessageCount = 5, DeadLetterCount = 1, Provider = CloudProviderType.Aws },
            new() { Name = "orders-topic", EntityType = "Topic", ActiveMessageCount = 0, DeadLetterCount = 0, Provider = CloudProviderType.Aws },
            new() { Name = "orders-topic/endpoint-queue", EntityType = "Subscription", ActiveMessageCount = 2, DeadLetterCount = 0, Provider = CloudProviderType.Aws },
        };

        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider.Setup(p => p.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));

        var controller = CreateController(messagingProviders: [awsProvider.Object]);

        var result = await controller.GetStats(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceStatsResponse>().Subject;
        response.TotalQueues.Should().Be(1);
        response.TotalTopics.Should().Be(1);
        response.TotalSubscriptions.Should().Be(1);
        response.TotalActive.Should().Be(7);
        response.TotalDlq.Should().Be(1);
    }

    /// <summary>
    /// Regression: AwsMessagingProvider labels SNS topics "SNS Topic", not "Topic", so the
    /// original exact-equality count reported zero topics for every AWS namespace no matter how
    /// many existed. The fixture above uses the literal string the provider actually emits.
    /// </summary>
    [Fact]
    public async Task GetStats_AwsSnsTopicEntityType_ShouldBeCountedAsATopic()
    {
        var ns = Namespace.Create("aws-ns", "AKIAFAKE:secretkey", "AWS Test", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var entities = new List<CloudEntity>
        {
            new() { Name = "orders", EntityType = "Queue", ActiveMessageCount = 5, Provider = CloudProviderType.Aws },
            new() { Name = "orders-events", EntityType = "SNS Topic", Provider = CloudProviderType.Aws },
            new() { Name = "billing-events", EntityType = "SNS Topic", Provider = CloudProviderType.Aws },
            new() { Name = "orders-events/orders", EntityType = "Subscription", Provider = CloudProviderType.Aws },
        };

        var awsProvider = new Mock<ICloudMessagingProvider>();
        awsProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        awsProvider.Setup(p => p.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));

        var controller = CreateController(messagingProviders: [awsProvider.Object]);

        var result = await controller.GetStats(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceStatsResponse>().Subject;
        response.TotalTopics.Should().Be(2);
        response.TotalQueues.Should().Be(1);
        response.TotalSubscriptions.Should().Be(1);
    }

    /// <summary>
    /// GCP Pub/Sub topics arrive as the bare "Topic" string, so the suffix match must not have
    /// regressed the provider that was already counted correctly.
    /// </summary>
    [Fact]
    public async Task GetStats_GcpTopicEntityType_ShouldStillBeCountedAsATopic()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", "GCP Test", provider: CloudProviderType.Gcp, gcpProjectId: "my-project").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var entities = new List<CloudEntity>
        {
            new() { Name = "patient-intake", EntityType = "Topic", Provider = CloudProviderType.Gcp },
            new() { Name = "patient-intake/sub", EntityType = "Subscription", Provider = CloudProviderType.Gcp },
        };

        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);
        gcpProvider.Setup(p => p.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));

        var controller = CreateController(messagingProviders: [gcpProvider.Object]);

        var result = await controller.GetStats(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceStatsResponse>().Subject;
        response.TotalTopics.Should().Be(1);
        response.TotalSubscriptions.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_GcpListEntitiesFails_ShouldReturnZeroedStats()
    {
        var ns = Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", "GCP Test", provider: CloudProviderType.Gcp, gcpProjectId: "my-project").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var gcpProvider = new Mock<ICloudMessagingProvider>();
        gcpProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);
        gcpProvider.Setup(p => p.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Failure(Error.ExternalService("GCP.PubSub.ListFailed", "unreachable")));

        var controller = CreateController(messagingProviders: [gcpProvider.Object]);

        var result = await controller.GetStats(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceStatsResponse>().Subject;
        response.Should().Be(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task GetStats_AwsProviderNotRegistered_ShouldReturnZeroedStats()
    {
        var ns = Namespace.Create("aws-ns", "AKIAFAKE:secretkey", "AWS Test", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var controller = CreateController(messagingProviders: []);

        var result = await controller.GetStats(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NamespaceStatsResponse>().Subject;
        response.Should().Be(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));
    }

    #endregion

    #region GetStatsBatch Tests

    private static QueueRuntimePropertiesDto CreateTestQueue(
        string name, long active = 0, long dlq = 0, long scheduled = 0)
    {
        return new QueueRuntimePropertiesDto(
            Name: name,
            ActiveMessageCount: active,
            DeadLetterMessageCount: dlq,
            ScheduledMessageCount: scheduled,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            SizeInBytes: 0,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue);
    }

    private void SetUpAzureWrapper(
        Namespace ns,
        IReadOnlyList<QueueRuntimePropertiesDto> queues,
        Func<CancellationToken, Task<Result<IReadOnlyList<QueueRuntimePropertiesDto>>>>? getQueues = null)
    {
        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("unprotected"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        if (getQueues is not null)
        {
            wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>())).Returns(getQueues);
        }
        else
        {
            wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(queues));
        }
        wrapper.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(new List<TopicRuntimePropertiesDto>()));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>())).Returns(wrapper.Object);
    }

    [Fact]
    public async Task GetStatsBatch_EmptyRequest_ReturnsEmptyArrayWithoutRepositoryCalls()
    {
        var result = await _controller.GetStatsBatch(new NamespaceStatsBatchRequest([]));

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = okResult.Value.Should().BeAssignableTo<IReadOnlyList<NamespaceStatsBatchEntry>>().Subject;
        entries.Should().BeEmpty();
        _namespaceRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatsBatch_MultipleOwnedAzureNamespaces_ReturnsStatsAndNamesForEach()
    {
        var ns1 = CreateTestNamespace("ns-one");
        var ns2 = CreateTestNamespace("ns-two");
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns1));
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns2));

        SetUpAzureWrapper(ns1, [CreateTestQueue("orders", active: 5, dlq: 1)]);
        SetUpAzureWrapper(ns2, [CreateTestQueue("billing", active: 2, scheduled: 3)]);

        var result = await _controller.GetStatsBatch(new NamespaceStatsBatchRequest([ns1.Id, ns2.Id]));

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = okResult.Value.Should().BeAssignableTo<IReadOnlyList<NamespaceStatsBatchEntry>>().Subject;
        entries.Should().HaveCount(2);

        var entry1 = entries.Single(e => e.NamespaceId == ns1.Id);
        entry1.Stats.TotalActive.Should().Be(5);
        entry1.Stats.TotalDlq.Should().Be(1);
        entry1.QueueNames.Should().BeEquivalentTo(["orders"]);

        var entry2 = entries.Single(e => e.NamespaceId == ns2.Id);
        entry2.Stats.TotalScheduled.Should().Be(3);
        entry2.QueueNames.Should().BeEquivalentTo(["billing"]);
    }

    /// <summary>
    /// Same "not found vs. inaccessible are indistinguishable" posture as
    /// <see cref="GetOwnedNamespaceAsync"/> for a single namespace — applied per item so one
    /// foreign/bad ID in a batch doesn't fail the whole request or leak which IDs exist.
    /// </summary>
    [Fact]
    public async Task GetStatsBatch_ForeignNamespaceId_IsSilentlyDroppedNotErrored()
    {
        var owned = CreateTestNamespace("owned-ns");
        var foreign = Namespace.Create(
            "foreign-ns",
            "Endpoint=sb://foreign.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Foreign NS",
            ownerId: "someone-else").Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(owned.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(owned));
        _namespaceRepository.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(foreign));

        SetUpAzureWrapper(owned, [CreateTestQueue("q1", active: 1)]);

        var result = await _controller.GetStatsBatch(new NamespaceStatsBatchRequest([owned.Id, foreign.Id]));

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = okResult.Value.Should().BeAssignableTo<IReadOnlyList<NamespaceStatsBatchEntry>>().Subject;
        entries.Should().ContainSingle();
        entries[0].NamespaceId.Should().Be(owned.Id);
    }

    [Fact]
    public async Task GetStatsBatch_DuplicateIds_AreDeduplicatedToOneEntry()
    {
        var ns = CreateTestNamespace("dup-ns");
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        SetUpAzureWrapper(ns, [CreateTestQueue("q1")]);

        var result = await _controller.GetStatsBatch(new NamespaceStatsBatchRequest([ns.Id, ns.Id, ns.Id]));

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = okResult.Value.Should().BeAssignableTo<IReadOnlyList<NamespaceStatsBatchEntry>>().Subject;
        entries.Should().ContainSingle();
        _namespaceRepository.Verify(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatsBatch_ExceedsMaxBatchSize_ReturnsValidationError()
    {
        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();

        var result = await _controller.GetStatsBatch(new NamespaceStatsBatchRequest(tooMany));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _namespaceRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatsBatch_ManyNamespaces_ComputesWithBoundedConcurrencyNotSequentially()
    {
        // Same fleet-scale finding as ServiceBusHealthCheck's own concurrency regression test:
        // a sequential foreach loop over N namespaces, each a slow live-provider round trip,
        // scales linearly with fleet size. Bounded concurrent computation (10 at a time) must
        // finish in a small fraction of the sequential time.
        const int namespaceCount = 20;
        var delay = TimeSpan.FromMilliseconds(150);
        var namespaces = Enumerable.Range(0, namespaceCount)
            .Select(i => CreateTestNamespace($"ns-{i}"))
            .ToArray();

        foreach (var ns in namespaces)
        {
            _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Namespace>.Success(ns));
            SetUpAzureWrapper(ns, [], getQueues: async ct =>
            {
                await Task.Delay(delay, ct);
                return Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                    new List<QueueRuntimePropertiesDto> { CreateTestQueue("q1", active: 1) });
            });
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _controller.GetStatsBatch(
            new NamespaceStatsBatchRequest(namespaces.Select(n => n.Id).ToList()));
        stopwatch.Stop();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = okResult.Value.Should().BeAssignableTo<IReadOnlyList<NamespaceStatsBatchEntry>>().Subject;
        entries.Should().HaveCount(namespaceCount);

        // Sequential would take >= namespaceCount * delay (3000ms). Bounded concurrency (10 at a
        // time) takes roughly ceil(20/10) * 150ms = 300ms. A generous ceiling comfortably
        // separates "parallel" from "sequential" without being flaky under CI load.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "namespace stats must be computed with bounded concurrency, not sequentially");
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_Success_ShouldReturnNoContent()
    {
        SetIntentHeaders(IntentHeaders.IntentDeleteNamespace);
        var ns = CreateTestNamespace();
        var id = ns.Id;
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _clientCache.Setup(c => c.Contains(id)).Returns(true);
        _clientCache.Setup(c => c.RemoveAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _namespaceRepository.Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Delete(id);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_NotFound_ShouldReturnNotFound()
    {
        SetIntentHeaders(IntentHeaders.IntentDeleteNamespace);
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound(ErrorCodes.Namespace.NotFound, "Not found")));

        var result = await _controller.Delete(id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_WithCachedClient_ShouldRemoveFromCache()
    {
        SetIntentHeaders(IntentHeaders.IntentDeleteNamespace);
        var ns = CreateTestNamespace();
        var id = ns.Id;
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _clientCache.Setup(c => c.Contains(id)).Returns(true);
        _clientCache.Setup(c => c.RemoveAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _namespaceRepository.Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await _controller.Delete(id);

        _clientCache.Verify(c => c.RemoveAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Share / RevokeShare Tests

    [Fact]
    public async Task Share_Success_ReturnsOkWithUpdatedNamespace()
    {
        SetIntentHeaders(IntentHeaders.IntentShareNamespace);
        var ns = CreateTestNamespace(); // owned by the default SPA owner, matching the controller's caller identity
        var id = ns.Id;
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _namespaceRepository.Setup(r => r.UpdateAsync(ns, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Share(id, new ShareNamespaceRequest("colleague-owner-id"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<NamespaceResponse>().Subject;
        response.SharedWithOwnerIds.Should().Contain("colleague-owner-id");
    }

    // Missing-intent-headers (428, via Problem()) is covered in the integration test suite —
    // Problem() requires ProblemDetailsFactory, which this Moq-based harness doesn't register
    // (same convention already established for Delete's equivalent path elsewhere in this file).

    [Fact]
    public async Task Share_NotOwner_ReturnsNotFound()
    {
        SetIntentHeaders(IntentHeaders.IntentShareNamespace);
        var ns = Namespace.Create(
            "other-owner-ns.servicebus.windows.net",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "someone-else").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var result = await _controller.Share(ns.Id, new ShareNamespaceRequest("colleague-owner-id"));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Share_WithOwnersOwnId_ReturnsBadRequest()
    {
        SetIntentHeaders(IntentHeaders.IntentShareNamespace);
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var result = await _controller.Share(ns.Id, new ShareNamespaceRequest(Namespace.SpaOwnerId));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RevokeShare_Success_ReturnsNoContent()
    {
        SetIntentHeaders(IntentHeaders.IntentShareNamespace);
        var ns = CreateTestNamespace();
        ns.ShareWith("colleague-owner-id");
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _namespaceRepository.Setup(r => r.UpdateAsync(ns, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RevokeShare(ns.Id, "colleague-owner-id");

        result.Should().BeOfType<NoContentResult>();
        ns.SharedWithOwnerIds.Should().NotContain("colleague-owner-id");
    }

    [Fact]
    public async Task RevokeShare_NotOwner_ReturnsNotFound()
    {
        SetIntentHeaders(IntentHeaders.IntentShareNamespace);
        var ns = Namespace.Create(
            "other-owner-ns2.servicebus.windows.net",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "someone-else").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var result = await _controller.RevokeShare(ns.Id, "colleague-owner-id");

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region Platform Event — Publisher Tests

    // ── Create ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Success_PublishesExactlyOnePlatformEvent()
    {
        // Arrange — full happy-path setup.
        var request = new CreateNamespaceRequest(
            "ns-event-test",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ConnectionAuthType.ConnectionString,
            "Event Test NS");

        _namespaceRepository
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _clientFactory
            .Setup(f => f.ValidateConnectionString(It.IsAny<string>()))
            .Returns(Result.Success());

        _connectionStringProtector
            .Setup(p => p.Protect(It.IsAny<string>()))
            .Returns(Result<string>.Success("PROTECTED:encrypted"));

        _namespaceRepository
            .Setup(r => r.AddAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        await _controller.Create(request);

        // Assert — exactly one event published with the correct EventType.
        _eventBus.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.NamespaceCreated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_SaveFails_PublishesZeroPlatformEvents()
    {
        // Arrange — repository save returns failure.
        var request = new CreateNamespaceRequest(
            "ns-event-fail",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ConnectionAuthType.ConnectionString,
            "Event Fail NS");

        _namespaceRepository
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _clientFactory
            .Setup(f => f.ValidateConnectionString(It.IsAny<string>()))
            .Returns(Result.Success());

        _connectionStringProtector
            .Setup(p => p.Protect(It.IsAny<string>()))
            .Returns(Result<string>.Success("PROTECTED:encrypted"));

        _namespaceRepository
            .Setup(r => r.AddAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Internal("SAVE_ERR", "Database unavailable")));

        // Act
        await _controller.Create(request);

        // Assert — no event published when the commit fails.
        _eventBus.Verify(
            b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Success_PublishesExactlyOnePlatformEvent()
    {
        // Arrange — full happy-path delete setup.
        SetIntentHeaders(IntentHeaders.IntentDeleteNamespace);
        var ns = CreateTestNamespace();
        var id = ns.Id;

        _namespaceRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _clientCache.Setup(c => c.Contains(id)).Returns(false);
        _namespaceRepository
            .Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        await _controller.Delete(id);

        // Assert — exactly one event published with the correct EventType.
        _eventBus.Verify(
            b => b.PublishAsync(
                It.Is<PlatformEvent>(e => e.EventType == EventTypes.NamespaceDeleted),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delete_RepositoryFails_PublishesZeroPlatformEvents()
    {
        // Arrange — DeleteAsync returns failure.
        SetIntentHeaders(IntentHeaders.IntentDeleteNamespace);
        var ns = CreateTestNamespace();
        var id = ns.Id;

        _namespaceRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));
        _clientCache.Setup(c => c.Contains(id)).Returns(false);
        _namespaceRepository
            .Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Internal("DEL_ERR", "Delete failed")));

        // Act
        await _controller.Delete(id);

        // Assert — no event published when delete fails.
        _eventBus.Verify(
            b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
