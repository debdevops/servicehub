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
        var error1 = Error.Validation("TEST", "Message 1");
        var error2 = Error.Validation("TEST", "Message 2");

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
        var error1 = Error.Validation("TEST", "Message 1");
        var error2 = Error.Validation("TEST", "Message 2");

        error1.GetHashCode().Should().Be(error2.GetHashCode());
    }
}
