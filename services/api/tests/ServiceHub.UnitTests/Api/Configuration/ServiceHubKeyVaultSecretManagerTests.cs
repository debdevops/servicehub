using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using ServiceHub.Api.Configuration;

namespace ServiceHub.UnitTests.Api.Configuration;

public sealed class ServiceHubKeyVaultSecretManagerTests
{
    private readonly ServiceHubKeyVaultSecretManager _sut = new();

    // ── Load ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_AnySecret_ReturnsTrue()
    {
        var props = new SecretProperties("any-secret");
        _sut.Load(props).Should().BeTrue();
    }

    // ── GetKey — Direct Mapping ──────────────────────────────────────

    [Fact]
    public void GetKey_EncryptionKey_MapsToSecurityEncryptionKey()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("servicehub-encryption-key"), "some-value");

        _sut.GetKey(secret).Should().Be("Security:EncryptionKey");
    }

    // ── GetKey — API Key Mapping ─────────────────────────────────────

    [Fact]
    public void GetKey_AdminApiKey_MapsToScopedApiKeys0()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("servicehub-api-key-admin"), "admin-key-value");

        _sut.GetKey(secret).Should().Be("Security:Authentication:ScopedApiKeys:0:Key");
    }

    [Fact]
    public void GetKey_ReadonlyApiKey_MapsToScopedApiKeys1()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("servicehub-api-key-readonly"), "ro-key-value");

        _sut.GetKey(secret).Should().Be("Security:Authentication:ScopedApiKeys:1:Key");
    }

    // ── GetKey — Default Mapping ─────────────────────────────────────

    [Fact]
    public void GetKey_UnknownSecret_UsesDefaultDoubleHyphonMapping()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("some--nested--setting"), "value");

        _sut.GetKey(secret).Should().Be("some:nested:setting");
    }

    [Fact]
    public void GetKey_SimpleUnknownSecret_ReturnsNameAsIs()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("simple-secret"), "value");

        _sut.GetKey(secret).Should().Be("simple-secret");
    }

    // ── GetKey — Case Insensitivity ──────────────────────────────────

    [Fact]
    public void GetKey_EncryptionKey_CaseInsensitive()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("ServiceHub-Encryption-Key"), "value");

        _sut.GetKey(secret).Should().Be("Security:EncryptionKey");
    }

    [Fact]
    public void GetKey_AdminApiKey_CaseInsensitive()
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("SERVICEHUB-API-KEY-ADMIN"), "value");

        _sut.GetKey(secret).Should().Be("Security:Authentication:ScopedApiKeys:0:Key");
    }
}
