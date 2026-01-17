namespace ServiceHub.Shared.Results;

/// <summary>
/// Represents the outcome of an operation that does not return a value.
/// Provides a functional approach to error handling without exceptions.
/// </summary>
public class Result
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error associated with a failed operation.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Gets all errors associated with a failed operation.
    /// </summary>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result"/> class.
    /// </summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The error if the operation failed.</param>
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
        Errors = error == Error.None ? [] : [error];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result"/> class with multiple errors.
    /// </summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="errors">The errors if the operation failed.</param>
    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
        Error = errors.Count > 0 ? errors[0] : Error.None;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result instance.</returns>
    public static Result Success() => new(true, Error.None);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    /// <param name="errors">The errors that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public static Result Failure(IReadOnlyList<Error> errors) => new(false, errors);

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A successful result instance with the value.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <typeparam name="TValue">The type of the expected value.</typeparam>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    /// <typeparam name="TValue">The type of the expected value.</typeparam>
    /// <param name="errors">The errors that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public static Result<TValue> Failure<TValue>(IReadOnlyList<Error> errors) => Result<TValue>.Failure(errors);

    /// <summary>
    /// Creates a result based on a condition.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="error">The error to use if the condition is false.</param>
    /// <returns>A success result if condition is true; otherwise, a failure result.</returns>
    public static Result Create(bool condition, Error error)
        => condition ? Success() : Failure(error);

    /// <summary>
    /// Combines multiple results into a single result.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>Success if all results are successful; otherwise, failure with all errors.</returns>
    public static Result Combine(params Result[] results)
    {
        var errors = results
            .Where(r => r.IsFailure)
            .SelectMany(r => r.Errors)
            .ToList();

        return errors.Count > 0 ? Failure(errors) : Success();
    }

    /// <summary>
    /// Matches the result to execute the appropriate function.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The result of the matched function.</returns>
    public T Match<T>(Func<T> onSuccess, Func<Error, T> onFailure)
        => IsSuccess ? onSuccess() : onFailure(Error);

    /// <summary>
    /// Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onFailure">Action to execute on failure.</param>
    public void Switch(Action onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
        {
            onSuccess();
        }
        else
        {
            onFailure(Error);
        }
    }
}

/// <summary>
/// Represents the outcome of an operation that returns a value of type <typeparamref name="TValue"/>.
/// Provides a functional approach to error handling without exceptions.
/// </summary>
/// <typeparam name="TValue">The type of the value returned on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>
    /// Gets the value if the operation succeeded.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing value on a failed result.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of a failed result. Check IsSuccess before accessing Value.");

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{TValue}"/> class.
    /// </summary>
    /// <param name="value">The value if successful.</param>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The error if the operation failed.</param>
    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{TValue}"/> class with multiple errors.
    /// </summary>
    /// <param name="value">The value if successful.</param>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="errors">The errors if the operation failed.</param>
    protected internal Result(TValue? value, bool isSuccess, IReadOnlyList<Error> errors)
        : base(isSuccess, errors)
    {
        _value = value;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result instance.</returns>
    public static Result<TValue> Success(TValue value) => new(value, true, Error.None);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public new static Result<TValue> Failure(Error error) => new(default, false, error);

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    /// <param name="errors">The errors that caused the failure.</param>
    /// <returns>A failed result instance.</returns>
    public new static Result<TValue> Failure(IReadOnlyList<Error> errors) => new(default, false, errors);

    /// <summary>
    /// Creates a result based on whether the value is not null.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="error">The error to use if the value is null.</param>
    /// <returns>A success result if value is not null; otherwise, a failure result.</returns>
    public static Result<TValue> Create(TValue? value, Error error)
        => value is not null ? Success(value) : Failure(error);

    /// <summary>
    /// Gets the value or a default if the result is a failure.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The value if successful; otherwise, the default value.</returns>
    public TValue GetValueOrDefault(TValue defaultValue)
        => IsSuccess ? _value! : defaultValue;

    /// <summary>
    /// Gets the value or computes a default if the result is a failure.
    /// </summary>
    /// <param name="defaultFactory">The factory function to compute the default value.</param>
    /// <returns>The value if successful; otherwise, the computed default value.</returns>
    public TValue GetValueOrDefault(Func<TValue> defaultFactory)
        => IsSuccess ? _value! : defaultFactory();

    /// <summary>
    /// Transforms the value if the result is successful.
    /// </summary>
    /// <typeparam name="TNew">The type of the transformed value.</typeparam>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A new result with the transformed value or the original error.</returns>
    public Result<TNew> Map<TNew>(Func<TValue, TNew> mapper)
        => IsSuccess
            ? Result<TNew>.Success(mapper(_value!))
            : Result<TNew>.Failure(Errors);

    /// <summary>
    /// Chains another operation that returns a result.
    /// </summary>
    /// <typeparam name="TNew">The type of the new result value.</typeparam>
    /// <param name="binder">The function that returns a new result.</param>
    /// <returns>The result of the bound function or the original error.</returns>
    public Result<TNew> Bind<TNew>(Func<TValue, Result<TNew>> binder)
        => IsSuccess ? binder(_value!) : Result<TNew>.Failure(Errors);

    /// <summary>
    /// Chains another operation that returns a result without a value.
    /// </summary>
    /// <param name="binder">The function that returns a new result.</param>
    /// <returns>The result of the bound function or the original error.</returns>
    public Result Bind(Func<TValue, Result> binder)
        => IsSuccess ? binder(_value!) : Result.Failure(Errors);

    /// <summary>
    /// Matches the result to execute the appropriate function.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The result of the matched function.</returns>
    public T Match<T>(Func<TValue, T> onSuccess, Func<Error, T> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(Error);

    /// <summary>
    /// Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onFailure">Action to execute on failure.</param>
    public void Switch(Action<TValue> onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
        {
            onSuccess(_value!);
        }
        else
        {
            onFailure(Error);
        }
    }

    /// <summary>
    /// Executes a side effect if the result is successful, then returns the original result.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>The original result.</returns>
    public Result<TValue> Tap(Action<TValue> action)
    {
        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    /// <param name="value">The value.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>
    /// Implicitly converts an error to a failed result.
    /// </summary>
    /// <param name="error">The error.</param>
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
