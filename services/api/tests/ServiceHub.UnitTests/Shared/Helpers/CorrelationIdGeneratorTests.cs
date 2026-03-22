using FluentAssertions;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Shared.Helpers;

public sealed class CorrelationIdGeneratorTests
{
    // ── Generate() ──────────────────────────────────────────────────

    [Fact]
    public void Generate_ReturnsStringStartingWithDefaultPrefix()
    {
        var id = CorrelationIdGenerator.Generate();
        id.Should().StartWith(CorrelationIdGenerator.Prefix);
    }

    [Fact]
    public void Generate_ReturnsUniqueIds()
    {
        var id1 = CorrelationIdGenerator.Generate();
        var id2 = CorrelationIdGenerator.Generate();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void Generate_ProducesValidId()
    {
        var id = CorrelationIdGenerator.Generate();
        CorrelationIdGenerator.IsValid(id).Should().BeTrue();
    }

    [Fact]
    public void Generate_ContainsOnlyAlphanumericAndHyphens()
    {
        var id = CorrelationIdGenerator.Generate();
        id.All(c => char.IsLetterOrDigit(c) || c == '-').Should().BeTrue();
    }

    // ── Generate(customPrefix) ───────────────────────────────────────

    [Fact]
    public void Generate_WithCustomPrefix_StartsWithThatPrefix()
    {
        var id = CorrelationIdGenerator.Generate("myapp");
        id.Should().StartWith("myapp-");
    }

    [Fact]
    public void Generate_WithEmptyPrefix_ThrowsArgumentException()
    {
        var act = () => CorrelationIdGenerator.Generate(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_WithWhitespacePrefix_ThrowsArgumentException()
    {
        var act = () => CorrelationIdGenerator.Generate("   ");
        act.Should().Throw<ArgumentException>();
    }

    // ── IsValid() ────────────────────────────────────────────────────

    [Fact]
    public void IsValid_NullInput_ReturnsFalse()
    {
        CorrelationIdGenerator.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void IsValid_EmptyString_ReturnsFalse()
    {
        CorrelationIdGenerator.IsValid(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsValid_TooShortString_ReturnsFalse()
    {
        CorrelationIdGenerator.IsValid("sh-abc").Should().BeFalse();
    }

    [Fact]
    public void IsValid_ContainsInvalidChars_ReturnsFalse()
    {
        CorrelationIdGenerator.IsValid("sh-abc@def-123456789").Should().BeFalse();
    }

    [Fact]
    public void IsValid_ValidGeneratedId_ReturnsTrue()
    {
        var id = CorrelationIdGenerator.Generate();
        CorrelationIdGenerator.IsValid(id).Should().BeTrue();
    }

    // ── GetOrGenerate() ──────────────────────────────────────────────

    [Fact]
    public void GetOrGenerate_ValidId_ReturnsThatId()
    {
        var existing = CorrelationIdGenerator.Generate();
        var result = CorrelationIdGenerator.GetOrGenerate(existing);
        result.Should().Be(existing);
    }

    [Fact]
    public void GetOrGenerate_NullInput_GeneratesNewId()
    {
        var result = CorrelationIdGenerator.GetOrGenerate(null);
        result.Should().NotBeNullOrEmpty();
        CorrelationIdGenerator.IsValid(result).Should().BeTrue();
    }

    [Fact]
    public void GetOrGenerate_InvalidInput_GeneratesNewId()
    {
        var result = CorrelationIdGenerator.GetOrGenerate("bad!");
        result.Should().NotBe("bad!");
        CorrelationIdGenerator.IsValid(result).Should().BeTrue();
    }
}
