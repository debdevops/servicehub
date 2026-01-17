using FluentAssertions;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Shared.Results;

public sealed class ResultTTests
{
    [Fact]
    public void Success_WhenCreated_ShouldContainValue()
    {
        var value = 42;

        var result = Result.Success(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(value);
    }

    [Fact]
    public void Failure_WhenCreated_ShouldNotContainValue()
    {
        var error = Error.Validation("TEST", "Test");

        var result = Result.Failure<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_WhenSuccess_ShouldReturnMappedValue()
    {
        var result = Result.Success(42);

        var mapped = result.Match(
            value => value * 2,
            _ => 0
        );

        mapped.Should().Be(84);
    }

    [Fact]
    public void Match_WhenFailure_ShouldExecuteOnFailure()
    {
        var error = Error.Validation("TEST", "Test");
        var result = Result.Failure<int>(error);

        var mapped = result.Match(
            value => value * 2,
            _ => -1
        );

        mapped.Should().Be(-1);
    }

    [Fact]
    public void ImplicitConversion_WhenConvertingValue_ShouldCreateSuccessResult()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitConversion_WhenConvertingError_ShouldCreateFailureResult()
    {
        var error = Error.Validation("TEST", "Test");

        Result<int> result = error;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}
