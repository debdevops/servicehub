using FluentAssertions;
using ServiceHub.Core.Models;
using Xunit;

namespace ServiceHub.UnitTests.Persistence;

public sealed class MessageGistTests
{
    [Fact]
    public void Nothing_recorded_gives_no_gist() =>
        MessageGist.From(null, null, null, null, null).Should().BeNull();

    [Fact]
    public void Body_is_one_line_and_clipped_and_properties_are_plain_values_only_at_most_three()
    {
        var body = "{\n  \"a\": " + new string('x', 300) + "\n}";
        var gist = MessageGist.From(body, "application/json", "c-1", null,
            "{\"a\":\"1\",\"b\":2,\"n\":{\"x\":1},\"c\":true,\"d\":\"4\"}");

        gist.Should().NotBeNull();
        gist!.Preview.Should().NotContain("\n").And.EndWith("…");
        gist.Preview!.Length.Should().BeLessThanOrEqualTo(141);
        gist.Properties.Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "true" });
    }

    [Fact]
    public void Malformed_properties_are_ignored() =>
        MessageGist.From("body", null, null, null, "{not json").Should().Match<MessageGist>(g => g.Properties == null && g.Preview == "body");
}
