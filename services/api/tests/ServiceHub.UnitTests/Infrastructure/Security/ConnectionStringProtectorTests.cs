using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class ConnectionStringProtectorTests
{
    private readonly Mock<ILogger<ConnectionStringProtector>> _loggerMock;
    private readonly IConfiguration _configuration;
    private const string ValidConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=secretkey123==";

    public ConnectionStringProtectorTests()
    {
        _loggerMock = new Mock<ILogger<ConnectionStringProtector>>();
        
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-encryption-key-for-unit-tests-32bytes-minimum-length",
                ["Security:EnableConnectionStringEncryption"] = "true"
            });
        _configuration = configurationBuilder.Build();
    }

    [Fact]
    public void Protect_WithValidConnectionString_ShouldReturnEncryptedString()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var result = protector.Protect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("ENC:V2:");
        result.Value.Should().NotContain(ValidConnectionString);
    }

    [Fact]
    public void Protect_WithEmptyString_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var result = protector.Protect(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Protect_WhenAlreadyEncrypted_ShouldReturnSameValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var result = protector.Protect(encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(encrypted);
    }

    [Fact]
    public void Unprotect_WithEncryptedString_ShouldReturnOriginalValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var result = protector.Unprotect(encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_WithUnprotectedString_ShouldReturnSameValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var result = protector.Unprotect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_WithEmptyString_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var result = protector.Unprotect(string.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Mask_WithConnectionString_ShouldMaskSecrets()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var masked = protector.Mask(ValidConnectionString);

        masked.Should().NotContain("secretkey123");
        masked.Should().Contain("***MASKED***");
    }

    [Fact]
    public void Mask_WithEncryptedString_ShouldReturnEncryptedIndicator()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var masked = protector.Mask(encrypted);

        masked.Should().Be("[ENCRYPTED]");
    }

    [Fact]
    public void Mask_WithEmptyString_ShouldReturnEmpty()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var masked = protector.Mask(string.Empty);

        masked.Should().BeEmpty();
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTrip_ShouldPreserveValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _loggerMock.Object);

        var protected1 = protector.Protect(ValidConnectionString);
        var unprotected = protector.Unprotect(protected1.Value);

        unprotected.IsSuccess.Should().BeTrue();
        unprotected.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Protect_WhenEncryptionDisabled_ShouldUseLegacyFormat()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-key",
                ["Security:EnableConnectionStringEncryption"] = "false"
            });
        var config = configurationBuilder.Build();
        var protector = new ConnectionStringProtector(config, _loggerMock.Object);

        var result = protector.Protect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("PROTECTED:");
        result.Value.Should().NotStartWith("ENC:V2:");
    }
}
