namespace ServiceHub.Api.Authorization;

/// <summary>
/// Defines API key permission scopes for granular access control.
/// Scopes follow the format: resource:action (e.g., "namespaces:read").
/// </summary>
public static class ApiKeyScopes
{
    // Namespace management scopes
    public const string NamespacesRead = "namespaces:read";
    public const string NamespacesWrite = "namespaces:write";

    // Message operations scopes
    public const string MessagesSend = "messages:send";
    public const string MessagesPeek = "messages:peek";

    // Queue management scopes
    public const string QueuesRead = "queues:read";

    // Topic management scopes
    public const string TopicsRead = "topics:read";

    // Subscription management scopes
    public const string SubscriptionsRead = "subscriptions:read";

    // Anomaly detection scopes
    public const string AnomaliesRead = "anomalies:read";

    // Message-shape drift detection scopes
    public const string DriftFindingsRead = "drift-findings:read";

    // Same-provider proactive correlation scopes
    public const string CorrelationFindingsRead = "correlation-findings:read";

    // Deterministic narration scopes (roadmap §5.B, I4)
    public const string NarrationsRead = "narrations:read";

    // DLQ Intelligence scopes
    public const string DlqRead = "dlq:read";
    public const string DlqWrite = "dlq:write";

    // Audit trail scopes
    public const string AuditRead = "audit:read";

    // Recovery Evidence Ledger scopes
    public const string RecoveryRead = "recovery:read";
    public const string RecoveryWrite = "recovery:write";

    // Administrative access (all operations)
    public const string Admin = "admin";

    /// <summary>
    /// Checks if a scope grants permission for another scope.
    /// Admin scope grants all permissions.
    /// </summary>
    public static bool Grants(string grantedScope, string requiredScope)
    {
        if (string.Equals(grantedScope, Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(grantedScope, requiredScope, StringComparison.OrdinalIgnoreCase);
    }
}
