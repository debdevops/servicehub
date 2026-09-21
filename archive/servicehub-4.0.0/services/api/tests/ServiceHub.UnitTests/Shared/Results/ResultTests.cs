using FluentAssertions;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Shared.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_WhenCreated_ShouldHaveSuccessState()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_WhenCreated_ShouldHaveFailureState()
    {
        var error = Error.Validation("TEST", "Test error");

        var result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Combine_WhenAllSuccessful_ShouldReturnSuccess()
    {
        var result1 = Result.Success();
        var result2 = Result.Success();
        var result3 = Result.Success();

        var combined = Result.Combine(result1, result2, result3);

        combined.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Combine_WhenAnyFailed_ShouldReturnFailureWithAllErrors()
    {
        var result1 = Result.Success();
        var result2 = Result.Failure(Error.Validation("ERR1", "Error 1"));
        var result3 = Result.Failure(Error.Validation("ERR2", "Error 2"));

        var combined = Result.Combine(result1, result2, result3);

        combined.IsFailure.Should().BeTrue();
        combined.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Match_WhenSuccess_ShouldExecuteOnSuccess()
    {
        var result = Result.Success();
        var executed = false;

        result.Match(
            () => { executed = true; return 42; },
            _ => 0
        );

        executed.Should().BeTrue();
    }

    [Fact]
    public void Match_WhenFailure_ShouldExecuteOnFailure()
    {
        var error = Error.Validation("TEST", "Test");
        var result = Result.Failure(error);
        Error? capturedError = null;

        result.Match(
            () => 42,
            e => { capturedError = e; return 0; }
        );

        capturedError.Should().Be(error);
    }

    [Fact]
    public void Create_WhenConditionTrue_ShouldReturnSuccess()
    {
        var result = Result.Create(true, Error.Validation("TEST", "Test"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenConditionFalse_ShouldReturnFailure()
    {
        var error = Error.Validation("TEST", "Test");
        var result = Result.Create(false, error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}
