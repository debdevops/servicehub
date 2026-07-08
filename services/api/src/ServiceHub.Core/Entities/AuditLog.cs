namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents a single immutable audit log entry for a critical operation.
/// Records who did what, when, on which resource, with what outcome.
/// Tenant-isolated via OwnerId — no cross-tenant reads are possible.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Primary key (GUID stored as TEXT in SQLite).</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp when the action was recorded.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Multi-tenant isolation key — matches OwnerId on all other tables.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Identity of the actor: user email, API key name, or "System:&lt;Rule&gt;" for automation.
    /// </summary>
    public required string UserIdentity { get; init; }

    /// <summary>
    /// Dot-separated action category and verb, e.g. "Messages.Replay", "Rule.Toggle", "Namespace.Connect".
    /// Kept short for efficient indexing.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>"Success", "Failure", or "Partial" (used when a batch partially succeeds).</summary>
    public required string Outcome { get; init; }

    // ─── Resource context ────────────────────────────────────────────────────

    /// <summary>Namespace the action targeted. Null for cross-namespace or system operations.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>
    /// Friendly namespace display name — snapshotted at write time so deleted namespaces
    /// still appear correctly in audit history.
    /// </summary>
    public string? NamespaceName { get; init; }

    /// <summary>Queue or topic path that was targeted, if applicable.</summary>
    public string? EntityName { get; init; }

    /// <summary>Cloud provider identifier: "azure", "aws", or "gcp".</summary>
    public string? CloudProvider { get; init; }

    /// <summary>Deployment environment: "Dev", "Uat", or "Prod".</summary>
    public string? Environment { get; init; }

    // ─── Details ─────────────────────────────────────────────────────────────

    /// <summary>Primary resource name: message ID, rule ID, API key ID, etc.</summary>
    public string? ResourceName { get; init; }

    /// <summary>Azure Service Bus sequence number if applicable.</summary>
    public long? SequenceNumber { get; init; }

    /// <summary>
    /// JSON-serialized metadata describing the operation context.
    /// Must never contain raw message bodies, connection strings, or secrets.
    /// </summary>
    public string? DetailsJson { get; init; }

    /// <summary>Sanitized error detail or exception message for failed operations.</summary>
    public string? ErrorDetails { get; init; }

    // ─── HTTP correlation ────────────────────────────────────────────────────

    /// <summary>Originating client IP address (respects X-Forwarded-For via middleware).</summary>
    public string? ClientIp { get; init; }

    /// <summary>User-Agent header value from the originating request.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Correlation ID from the request pipeline for cross-service tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>HTTP method (GET, POST, DELETE, etc.).</summary>
    public string? HttpMethod { get; init; }

    /// <summary>Request path, without query string.</summary>
    public string? HttpPath { get; init; }
}
