namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for on-demand and scheduled backups (roadmap F2). Bound from the
/// "Backup" section of appsettings.json.
/// </summary>
/// <remarks>
/// Scheduled backups are disabled by default (<see cref="ScheduledBackupIntervalHours"/> is 0) —
/// operators opt in explicitly, mirroring <see cref="AuditRetentionOptions"/>. An on-demand
/// backup is always available via <c>POST /api/v1/admin/backup</c> regardless of this setting.
/// </remarks>
public sealed class BackupOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Backup";

    /// <summary>
    /// Directory backup bundles are written to. Null/empty defaults to a "backups" subfolder
    /// under <c>DlqDatabase:DataDirectory</c> (or the app base "data" directory if that is
    /// also unset).
    /// </summary>
    public string? BackupDirectory { get; set; }

    /// <summary>
    /// How often the background worker takes a scheduled backup, in hours. 0 disables scheduled
    /// backups (the default) — an operator must explicitly configure an interval to opt in.
    /// </summary>
    public int ScheduledBackupIntervalHours { get; set; }

    /// <summary>
    /// Number of most-recent backup bundles to keep. Older bundles are deleted immediately
    /// after a new backup completes successfully.
    /// </summary>
    public int RetentionCount { get; set; } = 14;
}
