using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Infrastructure.ServiceBus;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

public sealed class ServiceBusClientCacheTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();
    private readonly ServiceBusClientCache _sut;

    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc123=";

    public ServiceBusClientCacheTests()
    {
        // Setup logger factory to return a mock logger
        _loggerFactoryMock
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _sut = new ServiceBusClientCache(_loggerFactoryMock.Object);
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLoggerFactory_Throws()
    {
        var act = () => new ServiceBusClientCache(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    // ═══════════════════════════════════════════════════════════════
    // GetOrCreate
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetOrCreate_ValidConnectionString_ReturnsClient()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();

        // Act
        var client = _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Assert
        client.Should().NotBeNull();
        client.NamespaceId.Should().Be(namespaceId);
    }

    [Fact]
    public void GetOrCreate_CallTwiceSameNamespace_ReturnsSameClient()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();

        // Act
        var client1 = _sut.GetOrCreate(namespaceId, ValidConnectionString);
        var client2 = _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Assert
        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void GetOrCreate_DifferentNamespaces_ReturnsDifferentClients()
    {
        // Arrange
        var namespaceId1 = Guid.NewGuid();
        var namespaceId2 = Guid.NewGuid();

        // Act
        var client1 = _sut.GetOrCreate(namespaceId1, ValidConnectionString);
        var client2 = _sut.GetOrCreate(namespaceId2, ValidConnectionString);

        // Assert
        client1.Should().NotBeSameAs(client2);
        client1.NamespaceId.Should().Be(namespaceId1);
        client2.NamespaceId.Should().Be(namespaceId2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrCreate_NullOrWhiteSpaceConnectionString_Throws(string? connectionString)
    {
        // Arrange
        var namespaceId = Guid.NewGuid();

        // Act
        var act = () => _sut.GetOrCreate(namespaceId, connectionString!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetOrCreate_WhenDisposed_Throws()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.DisposeAsync().GetAwaiter().GetResult();

        // Act
        var act = () => _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Contains
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Contains_ExistingNamespace_ReturnsTrue()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Act
        var result = _sut.Contains(namespaceId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_NonExistingNamespace_ReturnsFalse()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();

        // Act
        var result = _sut.Contains(namespaceId);

        // Assert
        result.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // RemoveAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemoveAsync_ExistingClient_RemovesAndDisposesSuccessfully()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Act
        await _sut.RemoveAsync(namespaceId);

        // Assert
        _sut.Contains(namespaceId).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_NonExistingClient_DoesNotThrow()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();

        // Act & Assert
        await _sut.RemoveAsync(namespaceId);  // Should not throw
    }

    [Fact]
    public async Task RemoveAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId, ValidConnectionString);
        var cts = new CancellationTokenSource();

        // Act
        await _sut.RemoveAsync(namespaceId, cts.Token);

        // Assert
        _sut.Contains(namespaceId).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // DisposeAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DisposeAsync_WithCachedClients_DisposesAll()
    {
        // Arrange
        var namespaceId1 = Guid.NewGuid();
        var namespaceId2 = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId1, ValidConnectionString);
        _sut.GetOrCreate(namespaceId2, ValidConnectionString);

        // Act
        await _sut.DisposeAsync();

        // Assert
        _sut.Contains(namespaceId1).Should().BeFalse();
        _sut.Contains(namespaceId2).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_CompletesSuccessfully()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Act & Assert
        await _sut.DisposeAsync();
        await _sut.DisposeAsync();  // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_ClearsCache()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        _sut.GetOrCreate(namespaceId, ValidConnectionString);

        // Act
        await _sut.DisposeAsync();

        // Assert
        _sut.Contains(namespaceId).Should().BeFalse();
    }
}
