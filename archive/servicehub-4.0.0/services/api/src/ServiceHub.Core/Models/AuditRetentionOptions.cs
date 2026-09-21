namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for automatic audit-log retention. Bound from the "Audit:Retention"
/// section of appsettings.json.
/// </summary>
/// <remarks>
/// Disabled by default — audit logs are kept forever unless an operator opts in, so upgrading
/// ServiceHub never silently deletes existing compliance records. Retention is an instance-wide
/// policy (like <c>Security:*</c> settings), not per-tenant: it sweeps every owner's audit logs,
/// mirroring how a real compliance retention policy is set once for a whole deployment.
/// </remarks>
public sealed class AuditRetentionOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Audit:Retention";

    /// <summary>Whether automatic audit-log retention is enabled. Defaults to off (keep forever).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Audit log entries older than this many days are purged. Only consulted when
    /// <see cref="Enabled"/> is true.
    /// </summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>How often the background sweep checks for expired entries, in hours.</summary>
    public int SweepIntervalHours { get; set; } = 24;
}
