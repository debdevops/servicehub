using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
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
    private readonly NamespacesController _controller;

    public NamespacesControllerTests()
    {
        _namespaceRepository = new Mock<INamespaceRepository>();
        _clientFactory = new Mock<IServiceBusClientFactory>();
        _clientCache = new Mock<IServiceBusClientCache>();
        _connectionStringProtector = new Mock<IConnectionStringProtector>();
        _logger = new Mock<ILogger<NamespacesController>>();

        _controller = new NamespacesController(
            _namespaceRepository.Object,
            _clientFactory.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
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
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _clientFactory.Setup(f => f.ValidateConnectionString(It.IsAny<string>()))
            .Returns(Result.Success());

        _connectionStringProtector.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns(Result<string>.Success("PROTECTED:encrypted-conn-string-data"));

        _namespaceRepository.Setup(r => r.AddAsync(It.IsAny<Namespace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
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
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { existingNs }));

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Create_MissingConnectionString_ShouldReturnBadRequest()
    {
        var request = new CreateNamespaceRequest(
            "test-namespace",
            null,
            ConnectionAuthType.ConnectionString);

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_Success_ShouldReturnNoContent()
    {
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
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound(ErrorCodes.Namespace.NotFound, "Not found")));

        var result = await _controller.Delete(id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_WithCachedClient_ShouldRemoveFromCache()
    {
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
}
