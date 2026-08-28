using ServiceHub.Core.Models.Backup;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Creates and enumerates backup bundles for the SQLite database and namespace JSON store
/// (roadmap F2). Restore is deliberately not part of this interface — restore is a manual,
/// operator-driven procedure (see docs/BACKUP-RESTORE.md), not an automated code path.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new timestamped backup bundle: a consistent SQLite snapshot (via
    /// <c>VACUUM INTO</c>), an integrity check of that snapshot, an independent copy of the
    /// namespace JSON store, and a manifest — then applies retention, deleting the oldest
    /// bundles beyond the configured count.
    /// </summary>
    Task<Result<BackupManifest>> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists existing backup bundles, newest first, by reading each bundle's manifest.
    /// </summary>
    Task<Result<IReadOnlyList<BackupSummary>>> ListBackupsAsync(CancellationToken cancellationToken = default);
}
