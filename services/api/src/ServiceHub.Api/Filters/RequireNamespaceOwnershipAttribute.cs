using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Defence-in-depth action filter that enforces tenant isolation for any action whose route or
/// query string carries a <c>namespaceId</c>. Controllers still call
/// <c>ApiControllerBase.GetOwnedNamespaceAsync</c> inline — that convention is what this filter
/// backstops, since a missed inline check is exactly the defect class this closes. Route values
/// are checked before the query string because Queues/Topics/Subscriptions controllers bind
/// <c>namespaceId</c> from the route while <see cref="ServiceHub.Api.Controllers.V1.MessagesController"/>
/// binds it from the query string; checking query-first would leave the latter unprotected while
/// appearing covered.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireNamespaceOwnershipAttribute : Attribute, IAsyncActionFilter
{
    private const string NamespaceIdKey = "namespaceId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rawNamespaceId = context.RouteData.Values.TryGetValue(NamespaceIdKey, out var routeValue)
            ? routeValue?.ToString()
            : null;

        if (string.IsNullOrEmpty(rawNamespaceId))
        {
            rawNamespaceId = context.HttpContext.Request.Query.TryGetValue(NamespaceIdKey, out var queryValue)
                ? queryValue.ToString()
                : null;
        }

        // No namespace identifier on this action at all — it is namespace-agnostic, leave it alone.
        if (string.IsNullOrEmpty(rawNamespaceId) || !Guid.TryParse(rawNamespaceId, out var namespaceId))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var namespaceRepository = context.HttpContext.RequestServices.GetRequiredService<INamespaceRepository>();
        var namespaceResult = await namespaceRepository
            .GetByIdAsync(namespaceId, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        var ownerId = context.HttpContext.Items.TryGetValue("OwnerId", out var ownerValue) && ownerValue is string ownerIdString
            ? ownerIdString
            : Namespace.SpaOwnerId;

        if (namespaceResult.IsFailure || !namespaceResult.Value.IsAccessibleBy(ownerId))
        {
            context.Result = BuildNotFoundResult(context.HttpContext, namespaceId);
            return;
        }

        await next().ConfigureAwait(false);
    }

    private static ObjectResult BuildNotFoundResult(HttpContext httpContext, Guid namespaceId)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not Found",
            Detail = $"Namespace with ID '{namespaceId}' was not found.",
            Type = $"https://httpstatuses.com/{StatusCodes.Status404NotFound}",
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["code"] = ErrorCodes.Namespace.NotFound;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        return new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status404NotFound };
    }
}
