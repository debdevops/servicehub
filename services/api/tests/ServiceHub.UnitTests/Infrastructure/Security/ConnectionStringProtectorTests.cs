using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class ConnectionStringProtectorTests
{
    private readonly Mock<ILogger<ConnectionStringProtector>> _loggerMock;
    private readonly Mock<IHostEnvironment> _environmentMock;
    private readonly IConfiguration _configuration;
    private const string ValidConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=secretkey123==";

    public ConnectionStringProtectorTests()
    {
        _loggerMock = new Mock<ILogger<ConnectionStringProtector>>();
        _environmentMock = new Mock<IHostEnvironment>();
        _environmentMock.Setup(e => e.EnvironmentName).Returns("Development");
        
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
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Protect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("ENC[v1]:");
        result.Value.Should().NotContain(ValidConnectionString);
    }

    [Fact]
    public void Protect_WithEmptyString_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Protect(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Protect_WhenAlreadyEncrypted_ShouldReturnSameValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var result = protector.Protect(encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(encrypted);
    }

    [Fact]
    public void Unprotect_WithEncryptedString_ShouldReturnOriginalValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var result = protector.Unprotect(encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_WithUnprotectedString_ShouldReturnSameValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Unprotect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_WithEmptyString_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Unprotect(string.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Mask_WithConnectionString_ShouldMaskSecrets()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var masked = protector.Mask(ValidConnectionString);

        masked.Should().NotContain("secretkey123");
        masked.Should().Contain("***MASKED***");
    }

    [Fact]
    public void Mask_WithEncryptedString_ShouldReturnEncryptedIndicator()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var masked = protector.Mask(encrypted);

        masked.Should().Be("[ENCRYPTED:v1]");
    }

    [Fact]
    public void Mask_WithEmptyString_ShouldReturnEmpty()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var masked = protector.Mask(string.Empty);

        masked.Should().BeEmpty();
    }

    [Fact]
    public void Mask_WithAwsCredential_ShouldMaskAccessKeyAndSecret()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var input = "aws_access_key_id = AKIAIOSFODNN7EXAMPLE\naws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

        var masked = protector.Mask(input);

        masked.Should().NotContain("AKIAIOSFODNN7EXAMPLE");
        masked.Should().NotContain("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        masked.Should().Contain("***MASKED***");
    }

    [Fact]
    public void Mask_WithGcpServiceAccountJson_ShouldMaskPrivateKey()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var input = "{\"type\":\"service_account\",\"private_key_id\":\"abcd1234efgh5678\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwEXAMPLEKEYMATERIAL\\n-----END PRIVATE KEY-----\\n\"}";

        var masked = protector.Mask(input);

        masked.Should().NotContain("abcd1234efgh5678");
        masked.Should().NotContain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwEXAMPLEKEYMATERIAL");
        masked.Should().Contain("***MASKED***");
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTrip_ShouldPreserveValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

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
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Protect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("PROTECTED:");
        result.Value.Should().NotStartWith("ENC:V2:");
    }

    // ── Legacy PROTECTED: format ──────────────────────────────────────

    [Fact]
    public void Protect_WithLegacyProtectedString_ShouldReEncryptToCurrentFormat()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        // PROTECTED: is just base64 of UTF-8 bytes
        var legacyBytes = System.Text.Encoding.UTF8.GetBytes(ValidConnectionString);
        var legacyProtected = $"PROTECTED:{Convert.ToBase64String(legacyBytes)}";

        var result = protector.Protect(legacyProtected);

        result.IsSuccess.Should().BeTrue();
        // Should be re-encrypted to the current versioned format
        result.Value.Should().StartWith("ENC[v1]:");
    }

    [Fact]
    public void Unprotect_WithLegacyProtectedString_ShouldReturnOriginalValue()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var legacyBytes = System.Text.Encoding.UTF8.GetBytes(ValidConnectionString);
        var legacyProtected = $"PROTECTED:{Convert.ToBase64String(legacyBytes)}";

        var result = protector.Unprotect(legacyProtected);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_WithInvalidLegacyProtectedString_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        // Invalid base64 payload
        var invalid = "PROTECTED:!!!not-valid-base64!!!";

        var result = protector.Unprotect(invalid);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Mask_WithLegacyProtectedString_ShouldMaskKey()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var legacyBytes = System.Text.Encoding.UTF8.GetBytes(ValidConnectionString);
        var legacyProtected = $"PROTECTED:{Convert.ToBase64String(legacyBytes)}";

        var masked = protector.Mask(legacyProtected);

        masked.Should().NotContain("secretkey123");
        masked.Should().Contain("***MASKED***");
    }

    [Fact]
    public void Mask_WithInvalidLegacyProtectedString_ShouldReturnProtectedIndicator()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var invalid = "PROTECTED:!!!not-valid-base64!!!";

        var masked = protector.Mask(invalid);

        masked.Should().Be("[PROTECTED]");
    }

    [Fact]
    public void Mask_WithLegacyV2EncryptedString_ShouldReturnLegacyEncryptedIndicator()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        // Use the ENC:V2: marker with some payload (we don't need it to be valid — just need the prefix)
        var fakeV2 = "ENC:V2:somebase64payload==";

        var masked = protector.Mask(fakeV2);

        masked.Should().Be("[ENCRYPTED:V2-LEGACY]");
    }

    // ── Unprotect legacy V2 ──────────────────────────────────────────

    [Fact]
    public void Unprotect_WithInvalidLegacyV2String_ShouldReturnFailure()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        // ENC:V2: prefix but garbage payload — wrong key / tampered
        var invalid = "ENC:V2:bm90dmFsaWQ="; // base64 of "notvalid" — too short to have valid nonce+ciphertext+tag

        var result = protector.Unprotect(invalid);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Protect_WithUndecryptableLegacyV2String_ShouldFailInsteadOfReEncryptingCiphertext()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        // ENC:V2: prefix but garbage payload — must not be silently re-encrypted as if it were plaintext.
        var invalid = "ENC:V2:bm90dmFsaWQ=";

        var result = protector.Protect(invalid);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Protect_WithInvalidLegacyProtectedString_ShouldFailInsteadOfReEncryptingCiphertext()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var invalid = "PROTECTED:!!!not-valid-base64!!!";

        var result = protector.Protect(invalid);

        result.IsFailure.Should().BeTrue();
    }

    // ── Namespace.ComputeConnectionStringHash ────────────────────────

    [Fact]
    public void ComputeConnectionStringHash_WithValue_ReturnsSha256Hex()
    {
        var hash = ServiceHub.Core.Entities.Namespace.ComputeConnectionStringHash(ValidConnectionString);

        hash.Should().NotBeNull();
        hash.Should().HaveLength(64); // SHA-256 = 32 bytes = 64 hex chars
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void ComputeConnectionStringHash_SameInput_ReturnsSameHash()
    {
        var hash1 = ServiceHub.Core.Entities.Namespace.ComputeConnectionStringHash(ValidConnectionString);
        var hash2 = ServiceHub.Core.Entities.Namespace.ComputeConnectionStringHash(ValidConnectionString);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeConnectionStringHash_NullOrEmpty_ReturnsNull()
    {
        ServiceHub.Core.Entities.Namespace.ComputeConnectionStringHash(null).Should().BeNull();
        ServiceHub.Core.Entities.Namespace.ComputeConnectionStringHash("").Should().BeNull();
    }

    // ── Result<T> functional methods ─────────────────────────────────

    [Fact]
    public void ResultT_GetValueOrDefault_OnSuccess_ReturnsValue()
    {
        var result = ServiceHub.Shared.Results.Result.Success(42);
        result.GetValueOrDefault(0).Should().Be(42);
    }

    [Fact]
    public void ResultT_GetValueOrDefault_OnFailure_ReturnsDefault()
    {
        var result = ServiceHub.Shared.Results.Result.Failure<int>(ServiceHub.Shared.Results.Error.Validation("E", "err"));
        result.GetValueOrDefault(99).Should().Be(99);
    }

    [Fact]
    public void ResultT_GetValueOrDefaultFactory_OnFailure_InvokesFactory()
    {
        var result = ServiceHub.Shared.Results.Result.Failure<int>(ServiceHub.Shared.Results.Error.Validation("E", "err"));
        result.GetValueOrDefault(() => 77).Should().Be(77);
    }

    [Fact]
    public void ResultT_Map_OnSuccess_TransformsValue()
    {
        var result = ServiceHub.Shared.Results.Result.Success(5);
        var mapped = result.Map(x => x * 10);
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(50);
    }

    [Fact]
    public void ResultT_Map_OnFailure_PropagatesError()
    {
        var err = ServiceHub.Shared.Results.Error.Validation("E", "err");
        var result = ServiceHub.Shared.Results.Result.Failure<int>(err);
        var mapped = result.Map(x => x * 10);
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(err);
    }

    [Fact]
    public void ResultT_Bind_OnSuccess_ChainsOperation()
    {
        var result = ServiceHub.Shared.Results.Result.Success(5);
        var bound = result.Bind(x => ServiceHub.Shared.Results.Result.Success(x.ToString()));
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("5");
    }

    [Fact]
    public void ResultT_Bind_OnFailure_PropagatesError()
    {
        var err = ServiceHub.Shared.Results.Error.Validation("E", "err");
        var result = ServiceHub.Shared.Results.Result.Failure<int>(err);
        var bound = result.Bind(x => ServiceHub.Shared.Results.Result.Success(x.ToString()));
        bound.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ResultT_BindToResult_OnSuccess_ChainsOperation()
    {
        var result = ServiceHub.Shared.Results.Result.Success(5);
        var bound = result.Bind(x => ServiceHub.Shared.Results.Result.Success());
        bound.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ResultT_Tap_OnSuccess_ExecutesSideEffect()
    {
        var result = ServiceHub.Shared.Results.Result.Success(42);
        var sideEffectValue = 0;
        var returned = result.Tap(v => sideEffectValue = v);
        sideEffectValue.Should().Be(42);
        returned.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ResultT_Tap_OnFailure_DoesNotExecuteSideEffect()
    {
        var result = ServiceHub.Shared.Results.Result.Failure<int>(ServiceHub.Shared.Results.Error.Validation("E", "err"));
        var sideEffectValue = 0;
        var returned = result.Tap(v => sideEffectValue = v);
        sideEffectValue.Should().Be(0);
        returned.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ResultT_Switch_OnSuccess_ExecutesOnSuccessAction()
    {
        var result = ServiceHub.Shared.Results.Result.Success(10);
        var captured = 0;
        result.Switch(v => captured = v, _ => { });
        captured.Should().Be(10);
    }

    [Fact]
    public void ResultT_Switch_OnFailure_ExecutesOnFailureAction()
    {
        var err = ServiceHub.Shared.Results.Error.Validation("E", "err");
        var result = ServiceHub.Shared.Results.Result.Failure<int>(err);
        ServiceHub.Shared.Results.Error? captured = null;
        result.Switch(_ => { }, e => captured = e);
        captured.Should().Be(err);
    }

    [Fact]
    public void ResultT_Create_WithNonNullValue_ReturnsSuccess()
    {
        var result = ServiceHub.Shared.Results.Result<string>.Create("hello",
            ServiceHub.Shared.Results.Error.Validation("E", "err"));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void ResultT_Create_WithNullValue_ReturnsFailure()
    {
        var err = ServiceHub.Shared.Results.Error.Validation("E", "null");
        var result = ServiceHub.Shared.Results.Result<string>.Create(null, err);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(err);
    }

    [Fact]
    public void Result_Switch_OnSuccess_ExecutesOnSuccessAction()
    {
        var result = ServiceHub.Shared.Results.Result.Success();
        var executed = false;
        result.Switch(() => executed = true, _ => { });
        executed.Should().BeTrue();
    }

    [Fact]
    public void Result_Switch_OnFailure_ExecutesOnFailureAction()
    {
        var err = ServiceHub.Shared.Results.Error.Validation("E", "err");
        var result = ServiceHub.Shared.Results.Result.Failure(err);
        ServiceHub.Shared.Results.Error? captured = null;
        result.Switch(() => { }, e => captured = e);
        captured.Should().Be(err);
    }

    // ── InMemoryNamespaceRepository – tenant isolation ────────────────

    [Fact]
    public async Task GetByOwnerAsync_ReturnsOnlyMatchingOwner()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { ["NamespaceRepository:DataDirectory"] = tempDir })
                .Build();
            var repo = new ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository>.Instance,
                config);

            var owner1Ns = ServiceHub.Core.Entities.Namespace.Create(
                "owner1-ns.servicebus.windows.net", ValidConnectionString, ownerId: "owner1").Value;
            var owner2Ns = ServiceHub.Core.Entities.Namespace.Create(
                "owner2-ns.servicebus.windows.net", ValidConnectionString, ownerId: "owner2").Value;
            await repo.AddAsync(owner1Ns);
            await repo.AddAsync(owner2Ns);

            var owner1Results = await repo.GetByOwnerAsync("owner1");
            var owner2Results = await repo.GetByOwnerAsync("owner2");

            owner1Results.IsSuccess.Should().BeTrue();
            owner1Results.Value.Should().HaveCount(1);
            owner1Results.Value[0].OwnerId.Should().Be("owner1");
            owner2Results.Value.Should().HaveCount(1);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExistsAsync_WithOwnerId_ReturnsTrueForMatchingOwnerOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { ["NamespaceRepository:DataDirectory"] = tempDir })
                .Build();
            var repo = new ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository>.Instance,
                config);

            var ns = ServiceHub.Core.Entities.Namespace.Create(
                "shared-ns.servicebus.windows.net", ValidConnectionString, ownerId: "owner1").Value;
            await repo.AddAsync(ns);

            var existsForOwner1 = await repo.ExistsAsync("shared-ns.servicebus.windows.net", "owner1");
            var existsForOwner2 = await repo.ExistsAsync("shared-ns.servicebus.windows.net", "owner2");
            var existsEmpty = await repo.ExistsAsync("", "owner1");

            existsForOwner1.Should().BeTrue();
            existsForOwner2.Should().BeFalse();
            existsEmpty.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateAsync_ChangingOwnerId_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { ["NamespaceRepository:DataDirectory"] = tempDir })
                .Build();
            var repo = new ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository>.Instance,
                config);

            // Add an owner1 namespace
            var ns = ServiceHub.Core.Entities.Namespace.Create(
                "change-owner-ns.servicebus.windows.net", ValidConnectionString, ownerId: "owner1").Value;
            await repo.AddAsync(ns);

            // Create a modified version that attempts to change the OwnerId via a field set via reflection
            var nsWithTamperedOwner = ServiceHub.Core.Entities.Namespace.Create(
                "change-owner-ns.servicebus.windows.net", ValidConnectionString, ownerId: "owner2").Value;
            // Force same Id
            var idProp = typeof(ServiceHub.Core.Entities.Namespace).GetProperty("Id");
            idProp!.SetValue(nsWithTamperedOwner, ns.Id);

            var result = await repo.UpdateAsync(nsWithTamperedOwner);

            result.IsFailure.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── GetKeyFingerprint (roadmap F2) ──────────────────────────────────────

    [Fact]
    public void GetKeyFingerprint_ReturnsStableValueAcrossCalls()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var first = protector.GetKeyFingerprint();
        var second = protector.GetKeyFingerprint();

        first.Should().Be(second);
    }

    [Fact]
    public void GetKeyFingerprint_HasExpectedFormat_AndNeverContainsKeyMaterial()
    {
        var protector = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var fingerprint = protector.GetKeyFingerprint();

        fingerprint.Should().StartWith("sha256:");
        fingerprint.Should().MatchRegex("^sha256:[0-9a-f]{16}$");
        fingerprint.Should().NotContain("test-encryption-key-for-unit-tests-32bytes-minimum-length");
    }

    [Fact]
    public void GetKeyFingerprint_DiffersAcrossDifferentKeys()
    {
        var protectorA = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        var configB = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "a-completely-different-test-key-32bytes-minimum-length",
                ["Security:EnableConnectionStringEncryption"] = "true"
            })
            .Build();
        var protectorB = new ConnectionStringProtector(configB, _environmentMock.Object, _loggerMock.Object);

        protectorA.GetKeyFingerprint().Should().NotBe(protectorB.GetKeyFingerprint());
    }

    [Fact]
    public void GetKeyFingerprint_SameKeyAcrossInstances_ProducesSameFingerprint()
    {
        var protectorA = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);
        var protectorB = new ConnectionStringProtector(_configuration, _environmentMock.Object, _loggerMock.Object);

        protectorA.GetKeyFingerprint().Should().Be(protectorB.GetKeyFingerprint());
    }

    // ── Multi-key registry / ENC[v2:kid=...] envelope (W0.3) ─────────────────

    private static IConfiguration BuildRegistryConfig(string activeKeyId, params (string Id, string Material, string Status)[] keys)
    {
        var keysJson = string.Join(",", keys.Select(k =>
            $$"""{ "Id": "{{k.Id}}", "Material": "{{k.Material}}", "Status": "{{k.Status}}" }"""));
        var registryJson = $$"""{ "ActiveKeyId": "{{activeKeyId}}", "Keys": [ {{keysJson}} ] }""";

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKeyRegistry"] = registryJson,
                ["Security:EnableConnectionStringEncryption"] = "true",
            })
            .Build();
    }

    [Fact]
    public void Protect_WithRegistryConfigured_ProducesV2EnvelopeWithActiveKid()
    {
        var config = BuildRegistryConfig("active-1", ("active-1", "key-material-one", "active"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);

        var result = protector.Protect(ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("ENC[v2:kid=active-1]:");
    }

    [Fact]
    public void ProtectAndUnprotect_V2RoundTrip_PreservesValue()
    {
        var config = BuildRegistryConfig("active-1", ("active-1", "key-material-one", "active"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);

        var encrypted = protector.Protect(ValidConnectionString);
        var decrypted = protector.Unprotect(encrypted.Value);

        decrypted.IsSuccess.Should().BeTrue();
        decrypted.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_V2WithMultipleKeys_DecryptsUsingKeyNamedInEnvelope_NotJustActive()
    {
        // Encrypt under "old-key" while it is active, then rotate active to "new-key" and
        // confirm the ciphertext still decrypts — this is the entire point of rotation.
        var oldActiveConfig = BuildRegistryConfig("old-key",
            ("old-key", "old-key-material", "active"),
            ("new-key", "new-key-material", "retired"));
        var protectorBeforeRotation = new ConnectionStringProtector(oldActiveConfig, _environmentMock.Object, _loggerMock.Object);
        var encryptedUnderOldKey = protectorBeforeRotation.Protect(ValidConnectionString).Value;
        encryptedUnderOldKey.Should().StartWith("ENC[v2:kid=old-key]:");

        var afterRotationConfig = BuildRegistryConfig("new-key",
            ("old-key", "old-key-material", "retired"),
            ("new-key", "new-key-material", "active"));
        var protectorAfterRotation = new ConnectionStringProtector(afterRotationConfig, _environmentMock.Object, _loggerMock.Object);

        var decrypted = protectorAfterRotation.Unprotect(encryptedUnderOldKey);

        decrypted.IsSuccess.Should().BeTrue();
        decrypted.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_V2WithUnknownKid_ReturnsFailureNamingTheMissingKeyId()
    {
        var config = BuildRegistryConfig("active-1", ("active-1", "key-material-one", "active"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);
        var tampered = "ENC[v2:kid=nonexistent-key]:c29tZWJhc2U2NHBheWxvYWQtdGhhdC1pcy1sb25nZW5vdWdo";

        var result = protector.Unprotect(tampered);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("nonexistent-key");
    }

    [Fact]
    public void Unprotect_V2WithTamperedKidInEnvelope_FailsAadVerification()
    {
        // The kid is bound as AAD — swapping the visible kid without re-encrypting must fail
        // decryption even when the swapped-to key ID *does* exist in the registry.
        var config = BuildRegistryConfig("key-a",
            ("key-a", "material-a", "active"),
            ("key-b", "material-b", "retired"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var tampered = encrypted.Replace("kid=key-a", "kid=key-b");

        var result = protector.Unprotect(tampered);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Protect_V2AlreadyActiveKid_ReturnsUnchanged()
    {
        var config = BuildRegistryConfig("active-1", ("active-1", "key-material-one", "active"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var result = protector.Protect(encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(encrypted);
    }

    [Fact]
    public void Protect_V2WithNonActiveKid_ReEncryptsToActiveKey()
    {
        var oldActiveConfig = BuildRegistryConfig("old-key",
            ("old-key", "old-key-material", "active"),
            ("new-key", "new-key-material", "retired"));
        var protectorBeforeRotation = new ConnectionStringProtector(oldActiveConfig, _environmentMock.Object, _loggerMock.Object);
        var encryptedUnderOldKey = protectorBeforeRotation.Protect(ValidConnectionString).Value;

        var afterRotationConfig = BuildRegistryConfig("new-key",
            ("old-key", "old-key-material", "retired"),
            ("new-key", "new-key-material", "active"));
        var protectorAfterRotation = new ConnectionStringProtector(afterRotationConfig, _environmentMock.Object, _loggerMock.Object);

        var result = protectorAfterRotation.Protect(encryptedUnderOldKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("ENC[v2:kid=new-key]:");
        protectorAfterRotation.Unprotect(result.Value).Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_LegacyV1FormatUnderRegistryThatIncludesLegacyKid_StillDecrypts()
    {
        // Encrypt in single-key (v1) mode first — this is what "existing data before rotation
        // was ever turned on" looks like.
        var singleKeyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "the-original-single-key-material",
                ["Security:EnableConnectionStringEncryption"] = "true",
            })
            .Build();
        var singleKeyProtector = new ConnectionStringProtector(singleKeyConfig, _environmentMock.Object, _loggerMock.Object);
        var v1Encrypted = singleKeyProtector.Protect(ValidConnectionString).Value;
        v1Encrypted.Should().StartWith("ENC[v1]:");

        // Now the operator turns on the registry, carrying the original key forward under the
        // reserved "legacy-v1" ID — per docs/ENCRYPTION-KEY-ROTATION.md §2.
        var registryConfig = BuildRegistryConfig("prod-active",
            (EncryptionKeyRegistry.LegacyKeyId, "the-original-single-key-material", "retired"),
            ("prod-active", "a-brand-new-key", "active"));
        var registryProtector = new ConnectionStringProtector(registryConfig, _environmentMock.Object, _loggerMock.Object);

        var decrypted = registryProtector.Unprotect(v1Encrypted);

        decrypted.IsSuccess.Should().BeTrue();
        decrypted.Value.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void Unprotect_LegacyV1FormatUnderRegistryMissingLegacyKid_FailsWithActionableError()
    {
        var registryConfig = BuildRegistryConfig("prod-active", ("prod-active", "a-brand-new-key", "active"));
        var protector = new ConnectionStringProtector(registryConfig, _environmentMock.Object, _loggerMock.Object);
        var v1Encrypted = "ENC[v1]:c29tZWJhc2U2NHBheWxvYWQtdGhhdC1pcy1sb25nZW5vdWdo";

        var result = protector.Unprotect(v1Encrypted);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain(EncryptionKeyRegistry.LegacyKeyId);
    }

    [Fact]
    public void Protect_V1FormatUnderRegistryWhereLegacyKidIsStillActive_ReturnsUnchanged()
    {
        var registryConfig = BuildRegistryConfig(EncryptionKeyRegistry.LegacyKeyId,
            (EncryptionKeyRegistry.LegacyKeyId, "the-key", "active"));
        var protector = new ConnectionStringProtector(registryConfig, _environmentMock.Object, _loggerMock.Object);
        var v1Encrypted = "ENC[v1]:c29tZWJhc2U2NHBheWxvYWQtdGhhdC1pcy1sb25nZW5vdWdo";

        // legacy-v1 is still active in this registry, so Protect() on an ENC[v1]: string is a
        // pass-through — it must not attempt to decrypt/re-encrypt garbage ciphertext.
        var result = protector.Protect(v1Encrypted);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(v1Encrypted);
    }

    [Fact]
    public void Mask_V2Encrypted_ReturnsEncryptedIndicatorWithKid()
    {
        var config = BuildRegistryConfig("active-1", ("active-1", "key-material-one", "active"));
        var protector = new ConnectionStringProtector(config, _environmentMock.Object, _loggerMock.Object);
        var encrypted = protector.Protect(ValidConnectionString).Value;

        var masked = protector.Mask(encrypted);

        masked.Should().Be("[ENCRYPTED:v2:kid=active-1]");
    }

    [Fact]
    public void GetKeyFingerprint_UnderRegistry_ReflectsActiveKeyOnly()
    {
        var configA = BuildRegistryConfig("k1", ("k1", "material-1", "active"), ("k2", "material-2", "retired"));
        var protectorA = new ConnectionStringProtector(configA, _environmentMock.Object, _loggerMock.Object);

        var configB = BuildRegistryConfig("k2", ("k1", "material-1", "retired"), ("k2", "material-2", "active"));
        var protectorB = new ConnectionStringProtector(configB, _environmentMock.Object, _loggerMock.Object);

        protectorA.GetKeyFingerprint().Should().NotBe(protectorB.GetKeyFingerprint());
    }
}
