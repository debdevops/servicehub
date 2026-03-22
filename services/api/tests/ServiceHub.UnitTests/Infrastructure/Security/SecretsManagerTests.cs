using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class SecretsManagerTests
{
    private readonly SecretsManager _sut = new(NullLogger<SecretsManager>.Instance);

    // ── GetSecretAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetSecretAsync_ExistingSecret_ReturnsValue()
    {
        await _sut.SetSecretAsync("api-key", "secret-value-123");

        var result = await _sut.GetSecretAsync("api-key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("secret-value-123");
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentSecret_ReturnsFailure()
    {
        var result = await _sut.GetSecretAsync("nonexistent-key");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretAsync_EmptyName_ReturnsValidationFailure()
    {
        var result = await _sut.GetSecretAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretAsync_WhitespaceName_ReturnsValidationFailure()
    {
        var result = await _sut.GetSecretAsync("   ");

        result.IsSuccess.Should().BeFalse();
    }

    // ── SetSecretAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SetSecretAsync_NewSecret_Succeeds()
    {
        var result = await _sut.SetSecretAsync("new-key", "new-value");

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
    public async Task SetSecretAsync_OverwritesExisting_Succeeds()
    {
        await _sut.SetSecretAsync("overwrite-key", "original");
        var result = await _sut.SetSecretAsync("overwrite-key", "updated");

        result.IsSuccess.Should().BeTrue();

        var fetched = await _sut.GetSecretAsync("overwrite-key");
        fetched.Value.Should().Be("updated");
    }

    // ── DeleteSecretAsync ────────────────────────────────────────────

    [Fact]
    public async Task DeleteSecretAsync_ExistingSecret_Succeeds()
    {
        await _sut.SetSecretAsync("delete-me", "value");

        var result = await _sut.DeleteSecretAsync("delete-me");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteSecretAsync_AfterDeletion_NotFound()
    {
        await _sut.SetSecretAsync("delete-me2", "value");
        await _sut.DeleteSecretAsync("delete-me2");

        var result = await _sut.GetSecretAsync("delete-me2");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_NonExistentSecret_ReturnsFailure()
    {
        var result = await _sut.DeleteSecretAsync("ghost-key");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_EmptyName_ReturnsValidationFailure()
    {
        var result = await _sut.DeleteSecretAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ExistingSecret_ReturnsTrue()
    {
        await _sut.SetSecretAsync("existskey", "value");

        var exists = await _sut.ExistsAsync("existskey");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentSecret_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync("no-such-key");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_EmptyName_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync(string.Empty);

        exists.Should().BeFalse();
    }
}
