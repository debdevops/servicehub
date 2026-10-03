using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.UnitTests.Infrastructure.Security;

public sealed class EncryptionKeyRegistryTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Load_WithOnlySingleKey_WrapsAsLegacySingleKeyRegistry()
    {
        var config = BuildConfig(new() { ["Security:EncryptionKey"] = "a-single-legacy-key" });

        var registry = EncryptionKeyRegistry.Load(config);

        registry.IsMultiKey.Should().BeFalse();
        registry.ActiveKeyId.Should().Be(EncryptionKeyRegistry.LegacyKeyId);
        registry.Keys.Should().ContainSingle(k => k.Id == EncryptionKeyRegistry.LegacyKeyId);
    }

    [Fact]
    public void Load_WithNeitherKeyNorRegistry_Throws()
    {
        var config = BuildConfig(new());

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Load_WithValidRegistry_ParsesActiveKeyAndAllKeys()
    {
        var json = """
        {
          "ActiveKeyId": "prod-2",
          "Keys": [
            { "Id": "legacy-v1", "Material": "old-key-material", "Status": "retired" },
            { "Id": "prod-2", "Material": "new-key-material", "Status": "active" }
          ]
        }
        """;
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var registry = EncryptionKeyRegistry.Load(config);

        registry.IsMultiKey.Should().BeTrue();
        registry.ActiveKeyId.Should().Be("prod-2");
        registry.Keys.Should().HaveCount(2);
        registry.GetActive().Id.Should().Be("prod-2");
        registry.Find("legacy-v1").Should().NotBeNull();
        registry.Find("does-not-exist").Should().BeNull();
    }

    [Fact]
    public void Load_RegistryTakesPrecedenceOverSingleKey()
    {
        var json = """{ "ActiveKeyId": "only", "Keys": [ { "Id": "only", "Material": "m" } ] }""";
        var config = BuildConfig(new()
        {
            ["Security:EncryptionKeyRegistry"] = json,
            ["Security:EncryptionKey"] = "should-be-ignored",
        });

        var registry = EncryptionKeyRegistry.Load(config);

        registry.IsMultiKey.Should().BeTrue();
        registry.ActiveKeyId.Should().Be("only");
    }

    [Fact]
    public void Load_WithMissingActiveKeyId_Throws()
    {
        var json = """{ "Keys": [ { "Id": "a", "Material": "m" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ActiveKeyId*");
    }

    [Fact]
    public void Load_WithActiveKeyIdNotInKeys_Throws()
    {
        var json = """{ "ActiveKeyId": "missing", "Keys": [ { "Id": "a", "Material": "m" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match any key*");
    }

    [Fact]
    public void Load_WithDuplicateKeyIds_Throws()
    {
        var json = """
        {
          "ActiveKeyId": "a",
          "Keys": [
            { "Id": "a", "Material": "m1" },
            { "Id": "a", "Material": "m2" }
          ]
        }
        """;
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*");
    }

    [Fact]
    public void Load_WithEmptyKeysArray_Throws()
    {
        var json = """{ "ActiveKeyId": "a", "Keys": [] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least one key*");
    }

    [Fact]
    public void Load_WithEmptyMaterial_Throws()
    {
        var json = """{ "ActiveKeyId": "a", "Keys": [ { "Id": "a", "Material": "" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty key material*");
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("has_underscore")]
    [InlineData("")]
    public void Load_WithInvalidKeyIdFormat_Throws(string keyId)
    {
        var json = $$"""{ "ActiveKeyId": "x", "Keys": [ { "Id": "{{keyId}}", "Material": "m" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Load_WithUnrecognizedStatus_Throws()
    {
        var json = """{ "ActiveKeyId": "a", "Keys": [ { "Id": "a", "Material": "m", "Status": "bogus" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*unrecognized status*");
    }

    [Fact]
    public void Load_WithMalformedJson_Throws()
    {
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = "{ not valid json" });

        var act = () => EncryptionKeyRegistry.Load(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Load_StatusIsCaseInsensitive()
    {
        var json = """{ "ActiveKeyId": "a", "Keys": [ { "Id": "a", "Material": "m", "Status": "ACTIVE" } ] }""";
        var config = BuildConfig(new() { ["Security:EncryptionKeyRegistry"] = json });

        var registry = EncryptionKeyRegistry.Load(config);

        registry.GetActive().Status.Should().Be(EncryptionKeyStatus.Active);
    }
}
