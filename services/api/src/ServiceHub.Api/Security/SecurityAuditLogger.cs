using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Security;

/// <summary>
/// Emits structured, redacted audit logs for critical operations.
/// </summary>
public sealed class SecurityAuditLogger : IAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;

    public SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void LogCriticalAction(
        HttpContext httpContext,
        string ownerId,
        string action,
        string outcome,
        Guid? namespaceId = null,
        EnvironmentType? environment = null,
        string? resourceName = null,
        long? sequenceNumber = null,
        string? detail = null)
    {
        var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? string.Empty;

        // SECURITY_AUDIT prefix enables deterministic SIEM filtering.
        _logger.LogWarning(
            "SECURITY_AUDIT action={Action} outcome={Outcome} owner={OwnerId} namespace={NamespaceId} environment={Environment} resource={ResourceName} sequence={SequenceNumber} method={Method} path={Path} correlationId={CorrelationId} detail={Detail}",
            LogRedactor.SanitiseForLog(action),
            LogRedactor.SanitiseForLog(outcome),
            LogRedactor.SanitiseForLog(ownerId),
            namespaceId?.ToString() ?? "n/a",
            environment?.ToString() ?? "n/a",
            LogRedactor.SanitiseForLog(resourceName ?? "n/a"),
            sequenceNumber?.ToString() ?? "n/a",
            LogRedactor.SanitiseForLog(method),
            LogRedactor.SanitiseForLog(path),
            LogRedactor.SanitiseForLog(correlationId),
            LogRedactor.SanitiseForLog(detail ?? "n/a"));
    }
}
