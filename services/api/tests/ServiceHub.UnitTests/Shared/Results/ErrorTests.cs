using FluentAssertions;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Shared.Results;

public sealed class ErrorTests
{
    [Fact]
    public void Validation_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Validation("TEST_CODE", "Test message");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("TEST_CODE");
        error.Message.Should().Be("Test message");
    }

    [Fact]
    public void NotFound_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.NotFound("TEST_CODE", "Not found");

        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Conflict_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Conflict("TEST_CODE", "Conflict");

        error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Unauthorized_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Unauthorized("TEST_CODE", "Unauthorized");

        error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public void Forbidden_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Forbidden("TEST_CODE", "Forbidden");

        error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public void BusinessRule_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.BusinessRule("TEST_CODE", "Rule violation");

        error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void Internal_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Internal("TEST_CODE", "Internal error");

        error.Type.Should().Be(ErrorType.Internal);
    }

    [Fact]
    public void None_ShouldBeSingleton()
    {
        var none1 = Error.None;
        var none2 = Error.None;

        none1.Should().BeSameAs(none2);
        none1.Code.Should().BeEmpty();
        none1.Message.Should().BeEmpty();
    }

    [Fact]
    public void Equality_WhenSameCodeAndType_ShouldBeEqual()
    {
        var error1 = Error.Validation("TEST", "Same Message");
        var error2 = Error.Validation("TEST", "Same Message");

        (error1 == error2).Should().BeTrue();
        error1.Equals(error2).Should().BeTrue();
    }

    [Fact]
    public void Equality_WhenDifferentCode_ShouldNotBeEqual()
    {
        var error1 = Error.Validation("TEST1", "Message");
        var error2 = Error.Validation("TEST2", "Message");

        (error1 == error2).Should().BeFalse();
        error1.Equals(error2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_WhenSameCodeAndType_ShouldBeSame()
    {
        var error1 = Error.Validation("TEST", "Same Message");
        var error2 = Error.Validation("TEST", "Same Message");

        error1.GetHashCode().Should().Be(error2.GetHashCode());
    }

    [Fact]
    public void ExternalService_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.ExternalService("EXT_ERROR", "External service failed");

        error.Type.Should().Be(ErrorType.ExternalService);
        error.Code.Should().Be("EXT_ERROR");
    }

    [Fact]
    public void Timeout_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.Timeout("TIMEOUT", "Request timed out");

        error.Type.Should().Be(ErrorType.Timeout);
    }

    [Fact]
    public void RateLimited_WhenCreated_ShouldHaveCorrectType()
    {
        var error = Error.RateLimited("RATE_LIMIT", "Too many requests");

        error.Type.Should().Be(ErrorType.RateLimited);
    }

    [Fact]
    public void WithDetails_WhenOriginalDetailsNull_ReturnsErrorWithNewDetails()
    {
        var error = Error.Validation("E", "msg"); // Details == null by default
        var extra = new Dictionary<string, object> { ["key"] = "value" };

        var result = error.WithDetails(extra);

        result.Details.Should().ContainKey("key");
    }

    [Fact]
    public void WithDetails_WhenOriginalDetailsNotNull_MergesDetails()
    {
        var existing = new Dictionary<string, object> { ["existing"] = 1 };
        var error = new Error("E", "msg", ErrorType.Validation, existing);
        var extra = new Dictionary<string, object> { ["new"] = 2 };

        var result = error.WithDetails(extra);

        result.Details.Should().ContainKey("existing");
        result.Details.Should().ContainKey("new");
    }
}
