using FluentAssertions;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class LogRedactorTests
{
    [Fact]
    public void Redact_WithSharedAccessKey_ShouldMaskKey()
    {
        var input = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKey=secretkey123==";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("secretkey123");
        result.Should().Contain("SharedAccessKey=***REDACTED***");
    }

    [Fact]
    public void Redact_WithSharedAccessSignature_ShouldMaskSignature()
    {
        var input = "SharedAccessSignature=sig123456";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("sig123456");
        result.Should().Contain("SharedAccessSignature=***REDACTED***");
    }

    [Fact]
    public void Redact_WithAccountKey_ShouldMaskKey()
    {
        var input = "AccountKey=storagekey123==";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("storagekey123");
        result.Should().Contain("AccountKey=***REDACTED***");
    }

    [Fact]
    public void Redact_WithPassword_ShouldMaskPassword()
    {
        var input = "Password=mypassword123";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("mypassword123");
        result.Should().Contain("Password=***REDACTED***");
    }

    [Fact]
    public void Redact_WithBearerToken_ShouldMaskToken()
    {
        var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test");
        result.Should().Contain("Bearer ***REDACTED***");
    }

    [Fact]
    public void Redact_WithEncryptedValue_ShouldShowEncryptedIndicator()
    {
        var input = "ENC:V2:base64encryptedvalue";

        var result = LogRedactor.Redact(input);

        result.Should().Be("[ENCRYPTED]");
    }

    [Fact]
    public void Redact_WithLegacyProtectedValue_ShouldShowProtectedIndicator()
    {
        var input = "PROTECTED:base64value";

        var result = LogRedactor.Redact(input);

        result.Should().Be("[PROTECTED]");
    }

    [Fact]
    public void Redact_WithMultipleSecrets_ShouldMaskAll()
    {
        var input = "ConnectionString: Endpoint=sb://test.servicebus.windows.net/;SharedAccessKey=key123; Password=pass456";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain("key123");
        result.Should().NotContain("pass456");
        result.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void Redact_WithNullValue_ShouldReturnEmpty()
    {
        var result = LogRedactor.Redact(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Redact_WithEmptyString_ShouldReturnEmpty()
    {
        var result = LogRedactor.Redact(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Redact_WithNoSecrets_ShouldReturnOriginal()
    {
        var input = "This is a normal log message without secrets";

        var result = LogRedactor.Redact(input);

        result.Should().Be(input);
    }

    [Theory]
    [InlineData("X-API-KEY: abc123def456", "abc123def456")]
    [InlineData("ApiKey=secret123", "secret123")]
    [InlineData("api_key: mykey789", "mykey789")]
    public void Redact_WithApiKeys_ShouldMaskKeys(string input, string secret)
    {
        var result = LogRedactor.Redact(input);

        result.Should().NotContain(secret);
        result.Should().Contain("***REDACTED***");
    }
}
