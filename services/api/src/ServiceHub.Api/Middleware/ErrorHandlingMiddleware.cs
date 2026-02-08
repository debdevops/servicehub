using System.Net;
using System.Text.Json;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for handling exceptions and converting them to appropriate HTTP responses.
/// Ensures no stack traces or sensitive information leaks to clients.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorHandlingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="environment">The host environment.</param>
    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected, don't log as error
            _logger.LogDebug("Request was cancelled by client");
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}",
            correlationId,
            context.Request.Path);

        var error = CreateErrorFromException(exception);
        var statusCode = MapErrorTypeToStatusCode(error.Type);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse(
            error.Code,
            _environment.IsDevelopment() ? error.Message : GetSafeErrorMessage(error),
            correlationId,
            _environment.IsDevelopment() ? exception.StackTrace : null
        );

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static Error CreateErrorFromException(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => Error.Validation("Validation.NullArgument", exception.Message),
            ArgumentException => Error.Validation("Validation.InvalidArgument", exception.Message),
            UnauthorizedAccessException => Error.Unauthorized("Auth.Unauthorized", "Access denied."),
            TimeoutException => Error.Timeout("Operation.Timeout", "The operation timed out."),
            NotSupportedException => Error.Internal("Operation.NotSupported", exception.Message),
            NotImplementedException => Error.Internal("Operation.NotImplemented", "This feature is not yet implemented."),
            _ => Error.Internal("Internal.UnexpectedError", "An unexpected error occurred.")
        };
    }

    private static HttpStatusCode MapErrorTypeToStatusCode(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation => HttpStatusCode.BadRequest,
            ErrorType.NotFound => HttpStatusCode.NotFound,
            ErrorType.Conflict => HttpStatusCode.Conflict,
            ErrorType.Unauthorized => HttpStatusCode.Unauthorized,
            ErrorType.Forbidden => HttpStatusCode.Forbidden,
            ErrorType.Internal => HttpStatusCode.InternalServerError,
            ErrorType.ExternalService => HttpStatusCode.BadGateway,
            ErrorType.Timeout => HttpStatusCode.GatewayTimeout,
            ErrorType.RateLimited => HttpStatusCode.TooManyRequests,
            ErrorType.BusinessRule => HttpStatusCode.UnprocessableEntity,
            _ => HttpStatusCode.InternalServerError
        };
    }

    private static string GetSafeErrorMessage(Error error)
    {
        // In production, return generic messages for internal errors
        return error.Type switch
        {
            ErrorType.Internal => "An internal error occurred. Please try again later.",
            ErrorType.ExternalService => "A service dependency is currently unavailable.",
            ErrorType.Timeout => "The operation timed out. Please try again.",
            _ => error.Message
        };
    }

    /// <summary>
    /// Represents an error response returned to clients.
    /// </summary>
    private sealed record ErrorResponse(
        string Code,
        string Message,
        string CorrelationId,
        string? StackTrace = null);
}
