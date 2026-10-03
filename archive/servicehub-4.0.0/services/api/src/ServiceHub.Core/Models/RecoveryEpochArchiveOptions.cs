namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for Recovery Evidence Ledger epoch archival (roadmap next-chapter
/// M5.2). Bound from the "RecoveryEpochArchive" section of appsettings.json.
/// </summary>
public sealed class RecoveryEpochArchiveOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "RecoveryEpochArchive";

    /// <summary>
    /// Directory sealed-epoch archive files are written to, one subfolder per owner. Null/empty
    /// defaults to a "recovery-archive" subfolder under <c>DlqDatabase:DataDirectory</c> (or the
    /// app base "data" directory if that is also unset) — mirroring <see cref="BackupOptions"/>'s
    /// own <c>BackupDirectory</c> resolution.
    /// </summary>
    public string? ArchiveDirectory { get; set; }
}
