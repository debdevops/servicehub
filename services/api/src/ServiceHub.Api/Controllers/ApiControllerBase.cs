using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Api.Controllers;

/// <summary>
/// What every product controller shares: who is asking, which namespaces they may see, and how a
/// failure becomes a response.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The owner every query is scoped to: the identity middleware's, when one recognised the
    /// caller; otherwise the one browser session. Until Users exist, every caller shares it.
    /// </summary>
    protected virtual string OwnerId =>
        HttpContext.Items.TryGetValue("OwnerId", out var value) && value is string owner ? owner : Namespace.SpaOwnerId;

    /// <summary>
    /// The namespaces this caller's credential is restricted to, or null when unrestricted. A
    /// restricted caller must not be able to read, count or even confirm the existence of any
    /// other namespace. Set by an identity source that restricts (an OIDC <c>namespaces</c> claim).
    /// </summary>
    protected virtual IReadOnlySet<Guid>? AllowedNamespaceIds =>
        HttpContext.Items.TryGetValue("AllowedNamespaceIds", out var value) && value is IReadOnlySet<Guid> ids ? ids : null;

    /// <summary>
    /// How this request authenticated: <c>EasyAuth</c>, <c>Oidc</c>, <c>ApiKey</c>, or
    /// <c>session</c> when nothing was configured or presented.
    /// </summary>
    protected string AuthMethod =>
        HttpContext.Items.TryGetValue("AuthMethod", out var value) && value is string method ? method : "session";

    /// <summary>
    /// Who is acting, named only as far as the product actually knows (rule R6). This is the only
    /// way a controller obtains an actor.
    /// </summary>
    protected RecoveryActor Actor =>
        HttpContext.RequestServices.GetRequiredService<IActorIdentityResolver>().Resolve(ActorContextFactory.From(HttpContext));

    /// <summary>
    /// Loads a namespace the caller may see. A namespace that exists but belongs to someone else,
    /// or sits outside the caller's allow-list, is reported exactly like one that does not exist.
    /// </summary>
    protected async Task<Result<Namespace>> GetVisibleNamespaceAsync(
        INamespaceRepository repository, Guid id, CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        var ns = result.Value;
        var visible = string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal)
            && (AllowedNamespaceIds is null || AllowedNamespaceIds.Contains(ns.Id));

        return visible
            ? result
            : Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found."));
    }

    /// <summary>A failure as a ProblemDetails carrying its stable code.</summary>
    protected ObjectResult Problem(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Problem(StatusFor(error.Type), error.Code, error.Message);
    }

    /// <summary>A failure as a ProblemDetails with an explicit status.</summary>
    protected ObjectResult Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = detail,
            Instance = HttpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
        ErrorType.ExternalService => StatusCodes.Status502BadGateway,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request is not valid",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status428PreconditionRequired => "Confirmation required",
        StatusCodes.Status502BadGateway => "The cloud provider reported a problem",
        StatusCodes.Status503ServiceUnavailable => "Not available",
        _ => "The request could not be completed",
    };
}
