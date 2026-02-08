using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Exception filter that acts as a final safety net for unhandled exceptions.
/// Should rarely trigger since ErrorHandlingMiddleware handles most exceptions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    /// <inheritdoc/>
    public override void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
        {
            return;
        }

        var logger = context.HttpContext.RequestServices.GetService<ILogger<ApiExceptionFilterAttribute>>();
        var environment = context.HttpContext.RequestServices.GetService<IHostEnvironment>();
        var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

        logger?.LogError(context.Exception,
            "Unhandled exception in action filter. CorrelationId: {CorrelationId}",
            correlationId);

        var (statusCode, error) = MapException(context.Exception);
        var isDevelopment = environment?.IsDevelopment() ?? false;

        var problemDetails = new ProblemDetails
        {
            Type = GetProblemType(statusCode),
            Title = GetTitle(statusCode),
            Status = (int)statusCode,
            Detail = isDevelopment ? context.Exception.Message : error.Message,
            Instance = context.HttpContext.Request.Path,
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["errorCode"] = error.Code,
                ["traceId"] = context.HttpContext.TraceIdentifier
            }
        };

        if (isDevelopment && context.Exception.StackTrace is not null)
        {
            problemDetails.Extensions["stackTrace"] = context.Exception.StackTrace;
        }

        context.Result = new ObjectResult(problemDetails)
        {
            StatusCode = (int)statusCode
        };

        context.ExceptionHandled = true;
    }

    private static (HttpStatusCode StatusCode, Error Error) MapException(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => (HttpStatusCode.BadRequest, Error.Validation("Validation.NullArgument", exception.Message)),
            ArgumentException => (HttpStatusCode.BadRequest, Error.Validation("Validation.InvalidArgument", exception.Message)),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, Error.Unauthorized("Auth.Unauthorized", "Access denied.")),
            KeyNotFoundException => (HttpStatusCode.NotFound, Error.NotFound("Resource.NotFound", "The requested resource was not found.")),
            InvalidOperationException => (HttpStatusCode.Conflict, Error.Conflict("Operation.Invalid", exception.Message)),
            TimeoutException => (HttpStatusCode.GatewayTimeout, Error.Timeout("Operation.Timeout", "The operation timed out.")),
            NotSupportedException => (HttpStatusCode.NotImplemented, Error.Internal("Operation.NotSupported", "This operation is not supported.")),
            NotImplementedException => (HttpStatusCode.NotImplemented, Error.Internal("Operation.NotImplemented", "This feature is not yet implemented.")),
            OperationCanceledException => (HttpStatusCode.BadRequest, Error.Internal("Operation.Cancelled", "The operation was cancelled.")),
            _ => (HttpStatusCode.InternalServerError, Error.Internal("Internal.UnexpectedError", "An unexpected error occurred."))
        };
    }

    private static string GetProblemType(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            HttpStatusCode.Unauthorized => "https://tools.ietf.org/html/rfc7235#section-3.1",
            HttpStatusCode.Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            HttpStatusCode.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            HttpStatusCode.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            HttpStatusCode.UnprocessableEntity => "https://tools.ietf.org/html/rfc4918#section-11.2",
            HttpStatusCode.TooManyRequests => "https://tools.ietf.org/html/rfc6585#section-4",
            HttpStatusCode.InternalServerError => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            HttpStatusCode.NotImplemented => "https://tools.ietf.org/html/rfc7231#section-6.6.2",
            HttpStatusCode.BadGateway => "https://tools.ietf.org/html/rfc7231#section-6.6.3",
            HttpStatusCode.GatewayTimeout => "https://tools.ietf.org/html/rfc7231#section-6.6.5",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };
    }

    private static string GetTitle(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.Conflict => "Conflict",
            HttpStatusCode.UnprocessableEntity => "Unprocessable Entity",
            HttpStatusCode.TooManyRequests => "Too Many Requests",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            HttpStatusCode.NotImplemented => "Not Implemented",
            HttpStatusCode.BadGateway => "Bad Gateway",
            HttpStatusCode.GatewayTimeout => "Gateway Timeout",
            _ => "Error"
        };
    }
}
