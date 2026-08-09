using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers;

/// <summary>
/// Base controller providing common functionality for all API controllers.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The owner ID for the current request, derived from authentication context.
    /// Used for tenant isolation across all data-access operations.
    /// </summary>
    protected string OwnerId =>
        HttpContext.Items.TryGetValue("OwnerId", out var v) && v is string s
            ? s
            : Namespace.SpaOwnerId;

    /// <summary>
    /// The namespace allow-list carried by the current caller's credential, if any — set by
    /// <c>ApiKeyAuthenticationMiddleware</c>/<c>OidcBearerAuthenticationMiddleware</c> when the
    /// key/token restricts access to a subset of namespaces. Null means unrestricted.
    /// </summary>
    protected IReadOnlySet<Guid>? AllowedNamespaceIds =>
        HttpContext.Items.TryGetValue("AllowedNamespaceIds", out var v) && v is IReadOnlySet<Guid> ids
            ? ids
            : null;

    /// <summary>
    /// Fetches a namespace by ID and verifies <see cref="OwnerId"/> may access it — either as
    /// the namespace's owner, or because the owner explicitly shared it (see
    /// <see cref="Namespace.IsAccessibleBy(string)"/>) — so every controller enforces tenant isolation
    /// through this single, tested path instead of reimplementing the check inline. Returns the
    /// same NotFound failure whether the namespace doesn't exist or simply isn't accessible to
    /// the caller, so the two cases can't be distinguished from the response (avoids leaking
    /// namespace existence).
    /// <para>
    /// Use this for read/operate actions (browse, peek, replay, purge, Live Tail). For actions
    /// only the true owner may perform (delete, share, revoke), use
    /// <see cref="GetExclusivelyOwnedNamespaceAsync"/> instead.
    /// </para>
    /// </summary>
    protected async Task<Result<Namespace>> GetOwnedNamespaceAsync(
        INamespaceRepository namespaceRepository,
        Guid namespaceId,
        CancellationToken cancellationToken)
    {
        var namespaceResult = await namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (namespaceResult.IsFailure)
        {
            return namespaceResult;
        }

        if (!namespaceResult.Value.IsAccessibleBy(OwnerId, AllowedNamespaceIds))
        {
            return Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

        return namespaceResult;
    }

    /// <summary>
    /// Fetches a namespace by ID and verifies <see cref="OwnerId"/> is its <b>true owner</b> —
    /// a shared-with owner is not sufficient. Use this for privilege-sensitive actions a shared
    /// collaborator must not be able to perform: deleting the namespace, or changing who it's
    /// shared with. For everyday read/operate actions, use <see cref="GetOwnedNamespaceAsync"/>.
    /// </summary>
    protected async Task<Result<Namespace>> GetExclusivelyOwnedNamespaceAsync(
        INamespaceRepository namespaceRepository,
        Guid namespaceId,
        CancellationToken cancellationToken)
    {
        var namespaceResult = await namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (namespaceResult.IsFailure)
        {
            return namespaceResult;
        }

        var allowedNamespaceIds = AllowedNamespaceIds;
        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal)
            || (allowedNamespaceIds is not null && !allowedNamespaceIds.Contains(namespaceResult.Value.Id)))
        {
            return Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

        return namespaceResult;
    }

    /// <summary>
    /// Converts a Result to an appropriate ActionResult.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An ActionResult based on the result status.</returns>
    protected IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ToErrorResult(result.Error);
    }

    /// <summary>
    /// Converts a Result&lt;T&gt; to an appropriate ActionResult.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An ActionResult based on the result status.</returns>
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ToErrorResult(result.Error);
    }

    /// <summary>
    /// Converts an Error to an appropriate ActionResult for typed results.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="error">The error to convert.</param>
    /// <returns>An ActionResult based on the error type.</returns>
    protected ActionResult<T> ToActionResult<T>(Error error)
    {
        return ToErrorResult(error);
    }

    /// <summary>
    /// Converts a Result&lt;T&gt; to a Created ActionResult.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="actionName">The action name for the location header.</param>
    /// <param name="routeValues">The route values for the location header.</param>
    /// <returns>An ActionResult based on the result status.</returns>
    protected ActionResult<T> ToCreatedResult<T>(Result<T> result, string actionName, object? routeValues = null)
    {
        if (result.IsSuccess)
        {
            return CreatedAtAction(actionName, routeValues, result.Value);
        }

        return ToErrorResult(result.Error);
    }

    /// <summary>
    /// Converts a Result&lt;T&gt; to an Accepted ActionResult.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An ActionResult based on the result status.</returns>
    protected ActionResult<T> ToAcceptedResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Accepted(result.Value);
        }

        return ToErrorResult(result.Error);
    }

    /// <summary>
    /// Converts an Error to an appropriate ActionResult.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <returns>An ActionResult representing the error.</returns>
    private ActionResult ToErrorResult(Error error)
    {
        var problemDetails = CreateProblemDetails(error);

        return error.Type switch
        {
            ErrorType.Validation => BadRequest(problemDetails),
            ErrorType.NotFound => NotFound(problemDetails),
            ErrorType.Conflict => Conflict(problemDetails),
            ErrorType.Unauthorized => Unauthorized(problemDetails),
            ErrorType.Forbidden => new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status403Forbidden },
            ErrorType.RateLimited => new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status429TooManyRequests },
            ErrorType.Timeout => new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status504GatewayTimeout },
            ErrorType.ExternalService => new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status502BadGateway },
            _ => new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status500InternalServerError }
        };
    }

    /// <summary>
    /// Creates a ProblemDetails object from an Error.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <returns>A ProblemDetails object.</returns>
    private ProblemDetails CreateProblemDetails(Error error)
    {
        var (statusCode, title) = GetStatusCodeAndTitle(error.Type);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = error.Message,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = HttpContext.Request.Path
        };

        problemDetails.Extensions["code"] = error.Code;
        problemDetails.Extensions["traceId"] = HttpContext.TraceIdentifier;

        if (error.Details is not null && error.Details.Count > 0)
        {
            problemDetails.Extensions["details"] = error.Details;
        }

        return problemDetails;
    }

    /// <summary>
    /// Gets the HTTP status code and title for an error type.
    /// </summary>
    /// <param name="errorType">The error type.</param>
    /// <returns>A tuple containing the status code and title.</returns>
    private static (int StatusCode, string Title) GetStatusCodeAndTitle(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation Error"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.RateLimited => (StatusCodes.Status429TooManyRequests, "Rate Limited"),
            ErrorType.Timeout => (StatusCodes.Status504GatewayTimeout, "Gateway Timeout"),
            ErrorType.ExternalService => (StatusCodes.Status502BadGateway, "External Service Error"),
            ErrorType.Internal => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
            ErrorType.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "Business Rule Violation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };
    }
}
