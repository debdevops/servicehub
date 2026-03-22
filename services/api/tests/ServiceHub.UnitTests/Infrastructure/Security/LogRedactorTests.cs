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
        result.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Redact_WithEncryptedValue_ShouldShowEncryptedIndicator()
    {
        var input = "ENC:V2:base64encryptedvalue";

        var result = LogRedactor.Redact(input);

        result.Should().Be("[ENCRYPTED:V2]");
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

    // ═══════════════════════════════════════════════════════════════
    // Endpoint redaction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Redact_Endpoint_MasksButKeepsDomain()
    {
        var input = "Endpoint=sb://myns.servicebus.windows.net/";
        var result = LogRedactor.Redact(input);

        result.Should().Contain("myns.servicebus.windows.net");
        result.Should().Contain("***");
    }

    [Fact]
    public void Redact_InvalidEndpoint_MasksCompletely()
    {
        var input = "Endpoint=not-a-valid-uri";
        var result = LogRedactor.Redact(input);

        result.Should().Contain("***...***");
    }

    // ═══════════════════════════════════════════════════════════════
    // Encrypted value redaction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Redact_EncV2_ShouldShowEncryptedIndicator()
    {
        var input = "Connection: ENC:V2:abc123def456=";
        var result = LogRedactor.Redact(input);

        result.Should().Contain("[ENCRYPTED:V2]");
        result.Should().NotContain("abc123def456");
    }

    // ═══════════════════════════════════════════════════════════════
    // Authorization/X-API-Key header redaction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Redact_AuthorizationHeader_RedactsValue()
    {
        var input = "Authorization: Bearer eyJhbGciOi.payload.signature\r\nOther: safe";
        var result = LogRedactor.Redact(input);

        result.Should().NotContain("eyJhbGciOi");
    }

    [Fact]
    public void Redact_XApiKeyHeader_RedactsValue()
    {
        var input = "X-API-Key: secret-key-value-12345";
        var result = LogRedactor.Redact(input);

        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("secret-key-value-12345");
    }

    // ═══════════════════════════════════════════════════════════════
    // RedactForLogging
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RedactForLogging_NullValue_ReturnsNull()
    {
        var result = LogRedactor.RedactForLogging(null);
        result.Should().BeNull();
    }

    [Fact]
    public void RedactForLogging_StringValue_RedactsSecrets()
    {
        var result = LogRedactor.RedactForLogging("SharedAccessKey=mysecretkey123") as string;
        result.Should().NotBeNull();
        result.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void RedactForLogging_Exception_RedactsExceptionMessage()
    {
        var ex = new InvalidOperationException("SharedAccessKey=mysecretkey123");
        var result = LogRedactor.RedactForLogging(ex) as string;

        result.Should().NotBeNull();
        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("mysecretkey123");
    }

    [Fact]
    public void RedactForLogging_ExceptionWithInner_RedactsBothMessages()
    {
        var inner = new Exception("Password=secret123");
        var ex = new InvalidOperationException("SharedAccessKey=outerkey", inner);
        var result = LogRedactor.RedactForLogging(ex) as string;

        result.Should().NotBeNull();
        result.Should().NotContain("outerkey");
        result.Should().NotContain("secret123");
    }

    [Fact]
    public void RedactForLogging_Dictionary_RedactsSensitiveKeys()
    {
        var dict = new Dictionary<string, object>
        {
            ["Username"] = "admin",
            ["Password"] = "secret123",
            ["ApiKey"] = "key-value",
            ["Data"] = "SharedAccessKey=somekey"
        };

        var result = LogRedactor.RedactForLogging(dict) as IDictionary<string, object>;

        result.Should().NotBeNull();
        result!["Username"].Should().Be("admin");
        result["Password"].Should().Be("***REDACTED***");
        result["ApiKey"].Should().Be("***REDACTED***");
        // "Data" key is not sensitive by name, but its string value is redacted
        (result["Data"] as string).Should().Contain("***REDACTED***");
    }

    [Fact]
    public void RedactForLogging_Dictionary_SensitiveKeyVariants()
    {
        var dict = new Dictionary<string, object>
        {
            ["secret"] = "hidden",
            ["token"] = "hidden",
            ["credential"] = "hidden",
            ["connectionstring"] = "hidden",
        };

        var result = LogRedactor.RedactForLogging(dict) as IDictionary<string, object>;

        result.Should().NotBeNull();
        foreach (var kvp in result!)
        {
            kvp.Value.Should().Be("***REDACTED***");
        }
    }

    [Fact]
    public void RedactForLogging_NonStringNonExceptionNonDict_ReturnsOriginal()
    {
        var result = LogRedactor.RedactForLogging(42);
        result.Should().Be(42);
    }

    [Fact]
    public void RedactForLogging_Dictionary_NonStringValues_PreservedIfNotSensitiveKey()
    {
        var dict = new Dictionary<string, object>
        {
            ["Count"] = 42,
            ["Enabled"] = true,
        };

        var result = LogRedactor.RedactForLogging(dict) as IDictionary<string, object>;

        result.Should().NotBeNull();
        result!["Count"].Should().Be(42);
        result["Enabled"].Should().Be(true);
    }
}
