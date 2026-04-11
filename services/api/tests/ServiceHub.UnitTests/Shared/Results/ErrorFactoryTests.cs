using FluentAssertions;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Shared.Results;

public class ErrorFactoryTests
{
    [Fact]
    public void RateLimited_CreatesCorrectError()
    {
        var error = Error.RateLimited("RATE", "Too many requests");
        error.Code.Should().Be("RATE");
        error.Message.Should().Be("Too many requests");
        error.Type.Should().Be(ErrorType.RateLimited);
    }

    [Fact]
    public void BusinessRule_CreatesCorrectError()
    {
        var error = Error.BusinessRule("BIZ", "Rule violated");
        error.Code.Should().Be("BIZ");
        error.Message.Should().Be("Rule violated");
        error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void WithDetails_NoExistingDetails_SetsDetails()
    {
        var error = Error.Validation("V", "bad");
        var details = new Dictionary<string, object> { ["field"] = "name" };
        var updated = error.WithDetails(details);
        updated.Details.Should().ContainKey("field");
        updated.Details!["field"].Should().Be("name");
    }

    [Fact]
    public void WithDetails_ExistingDetails_MergesDetails()
    {
        var existing = new Dictionary<string, object> { ["a"] = 1 } as IReadOnlyDictionary<string, object>;
        var error = Error.Validation("V", "bad", existing);
        var additional = new Dictionary<string, object> { ["b"] = 2 };
        var updated = error.WithDetails(additional);
        updated.Details.Should().ContainKey("a");
        updated.Details.Should().ContainKey("b");
    }

    [Fact]
    public void RateLimited_WithDetails_SetsDetails()
    {
        var details = new Dictionary<string, object> { ["retryAfter"] = 30 } as IReadOnlyDictionary<string, object>;
        var error = Error.RateLimited("RATE", "slow down", details);
        error.Details.Should().ContainKey("retryAfter");
    }

    [Fact]
    public void BusinessRule_WithDetails_SetsDetails()
    {
        var details = new Dictionary<string, object> { ["rule"] = "max-items" } as IReadOnlyDictionary<string, object>;
        var error = Error.BusinessRule("BIZ", "exceeded", details);
        error.Details.Should().ContainKey("rule");
    }
}
