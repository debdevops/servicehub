namespace ServiceHub.Shared.Results;

/// <summary>
/// Represents the category of an error in the system.
/// Used to classify errors for appropriate handling and response generation.
/// </summary>
public enum ErrorType
{
    /// <summary>
    /// Indicates a validation failure due to invalid input data.
    /// </summary>
    Validation = 0,

    /// <summary>
    /// Indicates the requested resource was not found.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// Indicates a conflict with the current state of the resource.
    /// </summary>
    Conflict = 2,

    /// <summary>
    /// Indicates the operation is not authorized.
    /// </summary>
    Unauthorized = 3,

    /// <summary>
    /// Indicates the operation is forbidden for the current user.
    /// </summary>
    Forbidden = 4,

    /// <summary>
    /// Indicates an unexpected internal error occurred.
    /// </summary>
    Internal = 5,

    /// <summary>
    /// Indicates an external service or dependency failed.
    /// </summary>
    ExternalService = 6,

    /// <summary>
    /// Indicates the operation timed out.
    /// </summary>
    Timeout = 7,

    /// <summary>
    /// Indicates the request rate limit has been exceeded.
    /// </summary>
    RateLimited = 8,

    /// <summary>
    /// Indicates a business rule violation.
    /// </summary>
    BusinessRule = 9
}
