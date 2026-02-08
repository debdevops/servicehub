namespace ServiceHub.Shared.Results;

/// <summary>
/// Represents an error that occurred during an operation.
/// Immutable record providing structured error information.
/// </summary>
/// <param name="Code">The unique error code for identification and localization.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Type">The category of the error.</param>
/// <param name="Details">Optional additional details about the error.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, object>? Details = null)
{
    /// <summary>
    /// Represents no error. Used as a sentinel value.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Validation);

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A validation error instance.</returns>
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Validation, details);

    /// <summary>
    /// Creates a not found error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A not found error instance.</returns>
    public static Error NotFound(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.NotFound, details);

    /// <summary>
    /// Creates a conflict error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A conflict error instance.</returns>
    public static Error Conflict(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Conflict, details);

    /// <summary>
    /// Creates an unauthorized error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>An unauthorized error instance.</returns>
    public static Error Unauthorized(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Unauthorized, details);

    /// <summary>
    /// Creates a forbidden error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A forbidden error instance.</returns>
    public static Error Forbidden(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Forbidden, details);

    /// <summary>
    /// Creates an internal error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>An internal error instance.</returns>
    public static Error Internal(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Internal, details);

    /// <summary>
    /// Creates an external service error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>An external service error instance.</returns>
    public static Error ExternalService(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.ExternalService, details);

    /// <summary>
    /// Creates a timeout error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A timeout error instance.</returns>
    public static Error Timeout(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.Timeout, details);

    /// <summary>
    /// Creates a rate limited error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A rate limited error instance.</returns>
    public static Error RateLimited(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.RateLimited, details);

    /// <summary>
    /// Creates a business rule violation error.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <returns>A business rule error instance.</returns>
    public static Error BusinessRule(string code, string message, IReadOnlyDictionary<string, object>? details = null)
        => new(code, message, ErrorType.BusinessRule, details);

    /// <summary>
    /// Creates a new error with additional details merged.
    /// </summary>
    /// <param name="additionalDetails">The additional details to include.</param>
    /// <returns>A new error instance with merged details.</returns>
    public Error WithDetails(IReadOnlyDictionary<string, object> additionalDetails)
    {
        if (Details is null)
        {
            return this with { Details = additionalDetails };
        }

        var merged = new Dictionary<string, object>(Details);
        foreach (var kvp in additionalDetails)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return this with { Details = merged };
    }
}
