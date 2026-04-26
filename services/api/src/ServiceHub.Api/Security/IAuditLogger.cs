using ServiceHub.Core.Enums;

namespace ServiceHub.Api.Security;

/// <summary>
/// Records structured security audit events for critical operations.
/// </summary>
public interface IAuditLogger
{
    void LogCriticalAction(
        HttpContext httpContext,
        string ownerId,
        string action,
        string outcome,
        Guid? namespaceId = null,
        EnvironmentType? environment = null,
        string? resourceName = null,
        long? sequenceNumber = null,
        string? detail = null);
}

/// <summary>
/// No-op fallback implementation used by tests that construct controllers directly.
/// </summary>
public sealed class NoOpAuditLogger : IAuditLogger
{
    public static readonly NoOpAuditLogger Instance = new();

    private NoOpAuditLogger()
    {
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
        // Intentionally no-op.
    }
}
