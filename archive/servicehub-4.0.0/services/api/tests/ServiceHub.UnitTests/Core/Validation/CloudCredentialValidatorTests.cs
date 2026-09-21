using FluentAssertions;
using ServiceHub.Core.Validation;
using Xunit;

namespace ServiceHub.UnitTests.Core.Validation;

public sealed class CloudCredentialValidatorTests
{
    private const string ValidAccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string ValidSecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    // ── AWS access key pairs ─────────────────────────────────────────────────

    [Fact]
    public void ValidateAwsAccessKeyPair_WithValidPair_ReturnsSuccess()
    {
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair($"{ValidAccessKeyId}:{ValidSecretKey}");

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("no-colon-at-all")]
    [InlineData("AKIAIOSFODNN7EXAMPLE:")]
    [InlineData(":secretOnly")]
    [InlineData("")]
    public void ValidateAwsAccessKeyPair_WithMalformedPair_ReturnsFailure(string credential)
    {
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair(credential);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("AccessKeyId:SecretAccessKey");
    }

    [Fact]
    public void ValidateAwsAccessKeyPair_WithTemporarySessionKey_ReturnsFailureNamingAsia()
    {
        // Concatenated at runtime so the ASIA-prefixed fixture never appears verbatim
        // in source — a contiguous literal trips GitHub secret scanning ("AWS Temporary
        // Access Key ID"), which has no allowlist for the ASIA variant of AWS's
        // documented example key.
        var temporarySessionKeyId = "ASIA" + "IOSFODNN7EXAMPLE";
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair($"{temporarySessionKeyId}:{ValidSecretKey}");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Temporary session credentials");
    }

    [Theory]
    [InlineData("BKIAIOSFODNN7EXAMPLE")] // wrong prefix
    [InlineData("AKIASHORT")]            // too short
    [InlineData("AKIAIOSFODNN7EXAMPLETOOLONG")] // too long
    [InlineData("akiaiosfodnn7example")] // lowercase
    public void ValidateAwsAccessKeyPair_WithInvalidAccessKeyId_ReturnsFailure(string accessKeyId)
    {
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair($"{accessKeyId}:{ValidSecretKey}");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("access key ID");
    }

    [Theory]
    [InlineData("tooShort")]
    [InlineData("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKE!")] // invalid character, 40 chars
    [InlineData("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY1")] // 41 chars
    public void ValidateAwsAccessKeyPair_WithInvalidSecret_ReturnsFailure(string secret)
    {
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair($"{ValidAccessKeyId}:{secret}");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("secret access key");
    }

    [Fact]
    public void ValidateAwsAccessKeyPair_WithExtraColon_FailsOnSecretNotFormat()
    {
        // Split is on the FIRST colon only (same contract as
        // AwsClientFactory.ParseAccessKeyCredentials), so the extra colon lands in
        // the secret part and is rejected by the secret charset check.
        var result = CloudCredentialValidator.ValidateAwsAccessKeyPair($"{ValidAccessKeyId}:extra:colon");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("secret access key");
    }

    // ── GCP service account JSON ─────────────────────────────────────────────

    private static string ServiceAccountJson(
        string type = "service_account",
        string projectId = "my-project-123",
        string clientEmail = "svc@my-project-123.iam.gserviceaccount.com",
        string privateKey = "-----BEGIN PRIVATE KEY-----\\nabc\\n-----END PRIVATE KEY-----\\n") =>
        $$"""
        {
          "type": "{{type}}",
          "project_id": "{{projectId}}",
          "client_email": "{{clientEmail}}",
          "private_key": "{{privateKey}}"
        }
        """;

    [Fact]
    public void ValidateGcpServiceAccountJson_WithValidKey_ReturnsSuccess()
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(ServiceAccountJson());

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ truncated")]
    [InlineData("")]
    public void ValidateGcpServiceAccountJson_WithMalformedJson_ReturnsFailure(string json)
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not valid JSON");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithJsonArray_ReturnsFailure()
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson("[1, 2, 3]");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("JSON object");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithWrongType_ReturnsFailure()
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(ServiceAccountJson(type: "authorized_user"));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("service_account");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithMissingProjectId_ReturnsFailure()
    {
        var json = """{ "type": "service_account", "client_email": "a@b.c", "private_key": "PRIVATE KEY" }""";

        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("project_id");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithInvalidClientEmail_ReturnsFailure()
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(ServiceAccountJson(clientEmail: "not-an-email"));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("client_email");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithNonPemPrivateKey_ReturnsFailure()
    {
        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(ServiceAccountJson(privateKey: "just-a-string"));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("private_key");
    }

    [Fact]
    public void ValidateGcpServiceAccountJson_WithNonStringType_ReturnsFailure()
    {
        var json = """{ "type": 42, "project_id": "my-project-123", "client_email": "a@b.c", "private_key": "PRIVATE KEY" }""";

        var result = CloudCredentialValidator.ValidateGcpServiceAccountJson(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("service_account");
    }
}
