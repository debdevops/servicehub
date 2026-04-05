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

    [Fact]
    public void Failure_WithMultipleErrors_StoresAllErrors()
    {
        var errors = new[] { Error.Validation("E1", "Err 1"), Error.Validation("E2", "Err 2") };
        var result = Result.Failure<int>(errors);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Create_WhenValueNotNull_ReturnsSuccess()
    {
        var result = Result<string>.Create("hello", Error.Validation("E", "msg"));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void Create_WhenValueNull_ReturnsFailure()
    {
        var err = Error.Validation("E", "msg");
        var result = Result<string>.Create(null, err);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(err);
    }

    [Fact]
    public void GetValueOrDefault_ReturnsValue_OnSuccess()
    {
        var result = Result.Success(99);
        result.GetValueOrDefault(0).Should().Be(99);
    }

    [Fact]
    public void GetValueOrDefault_ReturnsDefault_OnFailure()
    {
        var result = Result.Failure<int>(Error.Validation("E", "msg"));
        result.GetValueOrDefault(-1).Should().Be(-1);
    }

    [Fact]
    public void GetValueOrDefault_Factory_ReturnsValue_OnSuccess()
    {
        var result = Result.Success(7);
        result.GetValueOrDefault(() => -1).Should().Be(7);
    }

    [Fact]
    public void GetValueOrDefault_Factory_ReturnsDefault_OnFailure()
    {
        var result = Result.Failure<int>(Error.Validation("E", "msg"));
        result.GetValueOrDefault(() => 42).Should().Be(42);
    }

    [Fact]
    public void Map_TransformsValue_OnSuccess()
    {
        var result = Result.Success(5).Map(v => v * 3);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(15);
    }

    [Fact]
    public void Map_PropagatesError_OnFailure()
    {
        var err = Error.Validation("E", "msg");
        var result = Result.Failure<int>(err).Map(v => v.ToString());
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(err);
    }

    [Fact]
    public void Bind_ExecutesBinder_OnSuccess()
    {
        var result = Result.Success(3).Bind(v => Result.Success(v + 1));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(4);
    }

    [Fact]
    public void Bind_PropagatesError_OnFailure()
    {
        var err = Error.Validation("E", "msg");
        var result = Result.Failure<int>(err).Bind(v => Result.Success(v + 1));
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Bind_ToResultWithoutValue_ExecutesBinder_OnSuccess()
    {
        var result = Result.Success(1).Bind(_ => Result.Success());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Bind_ToResultWithoutValue_PropagatesFailure()
    {
        var err = Error.Validation("E", "msg");
        var result = Result.Failure<int>(err).Bind(_ => Result.Success());
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Switch_ExecutesOnSuccess_WhenSuccess()
    {
        var called = false;
        Result.Success(10).Switch(_ => { called = true; }, _ => { });
        called.Should().BeTrue();
    }

    [Fact]
    public void Switch_ExecutesOnFailure_WhenFailure()
    {
        var called = false;
        Result.Failure<int>(Error.Validation("E", "msg")).Switch(_ => { }, _ => { called = true; });
        called.Should().BeTrue();
    }

    [Fact]
    public void Tap_ExecutesAction_OnSuccess()
    {
        var called = false;
        var result = Result.Success(42).Tap(_ => { called = true; });
        called.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Tap_DoesNotExecuteAction_OnFailure()
    {
        var called = false;
        var result = Result.Failure<int>(Error.Validation("E", "msg")).Tap(_ => { called = true; });
        called.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
    }
}
