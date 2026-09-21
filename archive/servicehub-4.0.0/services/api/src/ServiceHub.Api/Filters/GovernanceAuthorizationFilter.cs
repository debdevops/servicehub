using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Authorization filter that enforces <see cref="RequireGovernanceRoleAttribute"/> — the
/// Governance/RBAC enforcement layer (persistence design §14, master roadmap §6 item 3) reading
/// from the M3 grant schema via <see cref="IGovernanceAccessEvaluator"/>. Runs after
/// <see cref="ScopeAuthorizationFilter"/> in the same filter pipeline (see
/// <c>ServiceCollectionExtensions.AddApiServices</c>) and composes with it rather than replacing
/// it: a request must satisfy both the flat API-key scope and, where an action opts in, the
/// caller's specific Governance role.
/// </summary>
public sealed class GovernanceAuthorizationFilter : IAsyncAuthorizationFilter
{
    private const string NamespaceIdKey = "namespaceId";

    /// <summary>
    /// Audit action recorded when a caller's Governance role is insufficient. Namespaced like the
    /// intent-gate actions ("messages:replay", …) so it filters alongside them on the Audit page.
    /// </summary>
    internal const string GovernanceDeniedAction = "governance:role-denied";

    private readonly ILogger<GovernanceAuthorizationFilter> _logger;
    private readonly bool _authenticationEnabled;

    public GovernanceAuthorizationFilter(ILogger<GovernanceAuthorizationFilter> logger, IConfiguration configuration)
    {
        _logger = logger;
        _authenticationEnabled = configuration.GetValue("Security:Authentication:Enabled", false);
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Mirrors ScopeAuthorizationFilter: with authentication disabled there is no multi-tenant
        // concept to enforce a per-identity role against either.
        if (!_authenticationEnabled)
        {
            return;
        }

        var attribute = GetRequiredRoleAttribute(context);
        if (attribute is null)
        {
            return;
        }

        var httpContext = context.HttpContext;

        var ownerId = httpContext.Items.TryGetValue("OwnerId", out var ownerValue) && ownerValue is string ownerIdString
            ? ownerIdString
            : Namespace.SpaOwnerId;

        // Deliberately no SpaToken/EasyAuth bypass here (unlike ScopeAuthorizationFilter) — those
        // are exactly the identities Governance/RBAC exists to differentiate among multiple human
        // operators sharing one owner partition.
        var apiKeyName = httpContext.Items.TryGetValue("ApiKeyName", out var keyName) && keyName is string name
            ? name
            : null;

        var claimsIdentityName = httpContext.User?.Identity?.Name;

        var granteeIdentity = ActorIdentityResolver.ResolveHttpActor(apiKeyName, claimsIdentityName, ownerId).Identity;

        var namespaceId = ResolveNamespaceId(context);

        var evaluator = httpContext.RequestServices.GetRequiredService<IGovernanceAccessEvaluator>();
        var result = await evaluator.EvaluateAsync(
            ownerId, granteeIdentity, attribute.Role, namespaceId, attribute.PillarKind, httpContext.RequestAborted);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Governance authorization failed for {Method} {Path}: {Detail}",
                LogRedactor.SanitiseForLog(httpContext.Request.Method),
                LogRedactor.SanitiseForLog(httpContext.Request.Path),
                result.Error.Message);

            // Also record it in the persistent Audit Trail, not only the application log: a
            // credential refused a privileged action is exactly the "access event" the Audit
            // Trail is documented to be the complete record of, and the weaker intent-header
            // gate already writes its refusals there with the same "Denied" outcome. Without
            // this, a rejected privilege escalation is invisible to the product's own forensics
            // surface (Audit page, CSV export, Outcome=Denied filter).
            httpContext.RequestServices.GetService<IAuditLogger>()?.LogCriticalAction(
                httpContext,
                ownerId,
                action: GovernanceDeniedAction,
                outcome: "Denied",
                namespaceId: namespaceId,
                resourceName: httpContext.Request.Path.Value,
                detail: result.Error.Message);

            context.Result = BuildForbidden(httpContext, result.Error.Message);
        }
    }

    private static RequireGovernanceRoleAttribute? GetRequiredRoleAttribute(AuthorizationFilterContext context) =>
        context.ActionDescriptor.EndpointMetadata
            .OfType<RequireGovernanceRoleAttribute>()
            .LastOrDefault();

    private static Guid? ResolveNamespaceId(AuthorizationFilterContext context)
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

        return !string.IsNullOrEmpty(rawNamespaceId) && Guid.TryParse(rawNamespaceId, out var namespaceId)
            ? namespaceId
            : null;
    }

    private static JsonResult BuildForbidden(HttpContext httpContext, string detail) =>
        new(new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            title = "Forbidden",
            status = 403,
            detail,
            correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown"
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
}
