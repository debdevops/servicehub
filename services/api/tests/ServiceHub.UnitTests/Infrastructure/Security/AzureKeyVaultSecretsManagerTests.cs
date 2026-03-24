using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class AzureKeyVaultSecretsManagerTests
{
    private readonly Mock<SecretClient> _mockClient;
    private readonly AzureKeyVaultSecretsManager _sut;

    public AzureKeyVaultSecretsManagerTests()
    {
        _mockClient = new Mock<SecretClient>();
        _sut = new AzureKeyVaultSecretsManager(
            _mockClient.Object,
            NullLogger<AzureKeyVaultSecretsManager>.Instance);
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Ctor_NullClient_Throws()
    {
        var act = () => new AzureKeyVaultSecretsManager(
            null!, NullLogger<AzureKeyVaultSecretsManager>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("client");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new AzureKeyVaultSecretsManager(
            _mockClient.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetSecretAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetSecretAsync_ExistingSecret_ReturnsValue()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("my-secret"), "secret-value-123");
        _mockClient
            .Setup(c => c.GetSecretAsync("my-secret", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var result = await _sut.GetSecretAsync("my-secret");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("secret-value-123");
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentSecret_ReturnsFailure()
    {
        _mockClient
            .Setup(c => c.GetSecretAsync("not-found", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.GetSecretAsync("not-found");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretAsync_ServiceError_ReturnsFailure()
    {
        _mockClient
            .Setup(c => c.GetSecretAsync("err-key", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal Server Error"));

        var result = await _sut.GetSecretAsync("err-key");

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetSecretAsync_InvalidName_ReturnsValidationFailure(string? name)
    {
        var result = await _sut.GetSecretAsync(name!);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretAsync_NormalizesName_DotsToHyphens()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("my-dotted-name"), "value");
        _mockClient
            .Setup(c => c.GetSecretAsync("my-dotted-name", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var result = await _sut.GetSecretAsync("my.dotted.name");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("value");
    }

    // ── SetSecretAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SetSecretAsync_ValidInput_Succeeds()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("set-key"), "set-value");
        _mockClient
            .Setup(c => c.SetSecretAsync("set-key", "set-value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var result = await _sut.SetSecretAsync("set-key", "set-value");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetSecretAsync_EmptyName_ReturnsValidationFailure()
    {
        var result = await _sut.SetSecretAsync(string.Empty, "value");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SetSecretAsync_NullValue_ReturnsValidationFailure()
    {
        var result = await _sut.SetSecretAsync("key", null!);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SetSecretAsync_ServiceError_ReturnsFailure()
    {
        _mockClient
            .Setup(c => c.SetSecretAsync("err-set", "val", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

        var result = await _sut.SetSecretAsync("err-set", "val");

        result.IsSuccess.Should().BeFalse();
    }

    // ── DeleteSecretAsync ────────────────────────────────────────────

    [Fact]
    public async Task DeleteSecretAsync_ExistingSecret_Succeeds()
    {
        var operation = Mock.Of<DeleteSecretOperation>(
            o => o.HasCompleted == true);
        _mockClient
            .Setup(c => c.StartDeleteSecretAsync("del-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var result = await _sut.DeleteSecretAsync("del-key");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteSecretAsync_NonExistentSecret_ReturnsFailure()
    {
        _mockClient
            .Setup(c => c.StartDeleteSecretAsync("ghost", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.DeleteSecretAsync("ghost");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_EmptyName_ReturnsValidationFailure()
    {
        var result = await _sut.DeleteSecretAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_ServiceError_ReturnsFailure()
    {
        _mockClient
            .Setup(c => c.StartDeleteSecretAsync("err-del", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal error"));

        var result = await _sut.DeleteSecretAsync("err-del");

        result.IsSuccess.Should().BeFalse();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ExistingSecret_ReturnsTrue()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("exists-key"), "value");
        _mockClient
            .Setup(c => c.GetSecretAsync("exists-key", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var exists = await _sut.ExistsAsync("exists-key");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentSecret_ReturnsFalse()
    {
        _mockClient
            .Setup(c => c.GetSecretAsync("no-key", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var exists = await _sut.ExistsAsync("no-key");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_EmptyName_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync(string.Empty);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ServiceError_ReturnsFalse()
    {
        _mockClient
            .Setup(c => c.GetSecretAsync("err-exists", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Server error"));

        var exists = await _sut.ExistsAsync("err-exists");

        exists.Should().BeFalse();
    }

    // ── DI Registration ──────────────────────────────────────────────

    [Fact]
    public void DI_WithVaultUri_RegistersKeyVaultManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:VaultUri"] = "https://test-vault.vault.azure.net/"
            })
            .Build();

        ServiceHub.Infrastructure.DependencyInjection.AddSecurity(services, config);

        var provider = services.BuildServiceProvider();
        var manager = provider.GetService<ServiceHub.Core.Interfaces.ISecretsManager>();

        manager.Should().NotBeNull();
        manager.Should().BeOfType<AzureKeyVaultSecretsManager>();
    }

    [Fact]
    public void DI_WithoutVaultUri_RegistersInMemoryManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:VaultUri"] = ""
            })
            .Build();

        ServiceHub.Infrastructure.DependencyInjection.AddSecurity(services, config);

        var provider = services.BuildServiceProvider();
        var manager = provider.GetService<ServiceHub.Core.Interfaces.ISecretsManager>();

        manager.Should().NotBeNull();
        manager.Should().BeOfType<SecretsManager>();
    }

    [Fact]
    public void DI_NullConfig_RegistersInMemoryManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        ServiceHub.Infrastructure.DependencyInjection.AddSecurity(services, null);

        var provider = services.BuildServiceProvider();
        var manager = provider.GetService<ServiceHub.Core.Interfaces.ISecretsManager>();

        manager.Should().NotBeNull();
        manager.Should().BeOfType<SecretsManager>();
    }
}
