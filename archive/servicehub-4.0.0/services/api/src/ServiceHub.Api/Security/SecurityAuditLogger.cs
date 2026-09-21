using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Security;

/// <summary>
/// Emits structured, redacted audit logs for critical operations.
/// Writes to both the structured logger (for SIEM streaming) and the
/// persistent SQLite audit trail (for the in-app Audit Trail page).
/// </summary>
public sealed class SecurityAuditLogger : IAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;
    private readonly IAuditService _auditService;

    public SecurityAuditLogger(ILogger<SecurityAuditLogger> logger, IAuditService auditService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    /// <inheritdoc />
    public void LogCriticalAction(
        HttpContext httpContext,
        string ownerId,
        string action,
        string outcome,
        Guid? namespaceId = null,
        EnvironmentType? environment = null,
        string? entityName = null,
        string? cloudProvider = null,
        string? namespaceName = null,
        string? resourceName = null,
        long? sequenceNumber = null,
        string? detail = null)
    {
        var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var normalizedOutcome = NormalizeOutcome(outcome);

        // ── Structured text log (SECURITY_AUDIT prefix for SIEM filtering) ──
        // SECURITY_AUDIT prefix enables deterministic SIEM filtering.
        _logger.LogWarning(
            "SECURITY_AUDIT action={Action} outcome={Outcome} owner={OwnerId} namespace={NamespaceId} environment={Environment} entity={EntityName} cloud={CloudProvider} resource={ResourceName} sequence={SequenceNumber} method={Method} path={Path} correlationId={CorrelationId} detail={Detail}",
            LogRedactor.SanitiseForLog(action),
            LogRedactor.SanitiseForLog(normalizedOutcome),
            LogRedactor.SanitiseForLog(ownerId),
            namespaceId?.ToString() ?? "n/a",
            environment?.ToString() ?? "n/a",
            LogRedactor.SanitiseForLog(entityName ?? "n/a"),
            LogRedactor.SanitiseForLog(cloudProvider ?? "n/a"),
            LogRedactor.SanitiseForLog(resourceName ?? "n/a"),
            sequenceNumber?.ToString() ?? "n/a",
            LogRedactor.SanitiseForLog(method),
            LogRedactor.SanitiseForLog(path),
            LogRedactor.SanitiseForLog(correlationId),
            LogRedactor.SanitiseForLog(detail ?? "n/a"));

        // ── Persistent audit log (non-blocking channel enqueue) ──
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = ownerId,
            UserIdentity = ResolveUserIdentity(httpContext),
            Action = LogRedactor.SanitiseForLog(action),
            Outcome = normalizedOutcome,
            NamespaceId = namespaceId,
            NamespaceName = namespaceName,
            EntityName = entityName,
            CloudProvider = cloudProvider?.ToLowerInvariant(),
            Environment = environment?.ToString(),
            ResourceName = LogRedactor.SanitiseForLog(resourceName ?? string.Empty),
            SequenceNumber = sequenceNumber,
            DetailsJson = detail is null ? null : LogRedactor.Redact(detail),
            ClientIp = clientIp,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault(),
            CorrelationId = correlationId,
            HttpMethod = method,
            HttpPath = path,
        };

        _auditService.Enqueue(entry);
    }

    /// <summary>
    /// Maps caller-supplied outcome variants onto the canonical values the audit
    /// summary counts and the outcome filter expect ("Success", "Failure", "Partial").
    /// Values outside the known variants (e.g. "Denied", "Attempt") pass through unchanged.
    /// </summary>
    private static string NormalizeOutcome(string outcome) => outcome switch
    {
        "Succeeded" => "Success",
        "Failed" => "Failure",
        _ => outcome,
    };

    /// <summary>
    /// Resolves a human-readable identity for the actor from the HTTP context.
    /// Prefers the API key name stored in Items, then the claims principal,
    /// falling back to the request's OwnerId and finally "Unknown".
    /// </summary>
    private static string ResolveUserIdentity(HttpContext httpContext)
    {
        // Check for a named identity stored by the API key middleware
        if (httpContext.Items.TryGetValue("ApiKeyName", out var keyName) && keyName is string name && !string.IsNullOrEmpty(name))
            return $"ApiKey:{name}";

        // Check standard claims principal
        var user = httpContext.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(user))
            return user;

        // Fallback: use OwnerId as identity
        if (httpContext.Items.TryGetValue("OwnerId", out var owner) && owner is string ownerId)
            return ownerId;

        return "Unknown";
    }
}
