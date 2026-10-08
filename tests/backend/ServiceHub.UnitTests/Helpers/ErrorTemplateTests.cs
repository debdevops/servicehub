using FluentAssertions;
using ServiceHub.Core.Helpers;

namespace ServiceHub.UnitTests.Helpers;

/// <summary>Design 10 §5: the same cause gives the same template however its ids change; a different sentence gives a different one.</summary>
public sealed class ErrorTemplateTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   \r\n ", "")]
    [InlineData("Customer not found", "customer not found")]
    [InlineData("  Customer   not\r\nfound  ", "customer not found")]
    [InlineData("Order 12345 references customer 987", "order <n> references customer <n>")]
    [InlineData("Order 12345 failed", "order <n> failed")]
    [InlineData("Lock lost for 3f2504e0-4f89-41d3-9a0c-0305e82c3301", "lock lost for <id>")]
    [InlineData("LOCK LOST FOR 3F2504E0-4F89-41D3-9A0C-0305E82C3301", "lock lost for <id>")]
    [InlineData("Expired at 2026-10-08T12:34:56.789Z", "expired at <time>")]
    [InlineData("Expired at 2026-10-08 12:34:56+01:00", "expired at <time>")]
    [InlineData("Expired on 2026-10-08", "expired on <time>")]
    [InlineData("Timed out at 12:34:56", "timed out at <time>")]
    [InlineData("Bad checksum 9f86d081884c7d65", "bad checksum <hex>")]
    [InlineData("Handler feedface not ready", "handler feedface not ready")]
    [InlineData("Customer 'Acme Ltd' not found", "customer <q> not found")]
    [InlineData("Customer \"Acme Ltd\" not found", "customer <q> not found")]
    [InlineData("Can't reach the inventory service", "can't reach the inventory service")]
    [InlineData("Can't find 'sku-1' in stock", "can't find <q> in stock")]
    [InlineData("POST https://api.example.com:8443/v2/orders/55?x=1 returned 503", "post https://api.example.com returned <n>")]
    [InlineData("Called http://10.0.0.5/health", "called http://<n>.<n>.<n>.<n>")]
    public void Volatile_parts_become_placeholders_and_the_sentence_stays(string? input, string expected)
        => ErrorTemplate.Normalize(input).Should().Be(expected);

    [Fact]
    public void The_same_cause_with_different_ids_gives_the_same_template()
    {
        var a = ErrorTemplate.Normalize("Customer 'C-100' not found for order 5001 at 2026-10-08T10:00:00Z");
        var b = ErrorTemplate.Normalize("Customer 'C-999' not found for order 7777 at 2026-11-01T23:59:59Z");
        a.Should().Be(b);
    }

    [Fact]
    public void Different_sentences_give_different_templates()
    {
        ErrorTemplate.Normalize("Inventory service timed out after 30 seconds")
            .Should().NotBe(ErrorTemplate.Normalize("Customer 42 does not exist"));
    }

    [Fact]
    public void The_result_is_cut_at_the_limit_with_no_trailing_space()
    {
        var result = ErrorTemplate.Normalize(string.Join(' ', Enumerable.Repeat("word", 200)));
        result.Length.Should().BeLessThanOrEqualTo(ErrorTemplate.MaxLength);
        result.Should().NotEndWith(" ");
    }

    [Fact]
    public void A_huge_input_is_handled_quickly_and_never_throws()
    {
        var huge = new string('9', 500_000) + new string('a', 500_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var act = () => ErrorTemplate.Normalize(huge);
        act.Should().NotThrow();
        watch.ElapsedMilliseconds.Should().BeLessThan(2000);
    }

    [Fact]
    public void It_is_idempotent()
    {
        var once = ErrorTemplate.Normalize("Order 12345 for 'Acme' failed at 2026-10-08T10:00:00Z (3f2504e0-4f89-41d3-9a0c-0305e82c3301)");
        ErrorTemplate.Normalize(once).Should().Be(once);
    }

    [Fact]
    public void Text_that_is_only_volatile_gives_only_placeholders_not_an_empty_template()
        => ErrorTemplate.Normalize("12345").Should().Be("<n>");
}
