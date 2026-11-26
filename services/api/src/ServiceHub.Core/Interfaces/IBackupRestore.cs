namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Restore, the 4.1.0 way (unit 6.13): a bundle is checked, then <i>staged</i> — the live database is never swapped under a
/// running server. The next start moves the staged file into place before anything opens the database, and keeps the file it
/// replaced beside it.
/// </summary>
public interface IBackupRestore
{
    /// <summary>
    /// Everything that would stop this bundle from coming back, in words: the file is not what the manifest says, the snapshot
    /// is damaged, it was made with another encryption key, its schema is newer than this build, or its ledger chain does not
    /// verify. Nothing is changed.
    /// </summary>
    Task<RestoreCheck> CheckAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>Checks again and, only if it passes, stages the bundle for the next start.</summary>
    Task<RestoreCheck> StageAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages "start fresh" for the next start: the current database is set aside and ServiceHub begins with an empty one. The caller
    /// must already hold a backup of the current state (<paramref name="safetyBackupId"/>) — a fresh start is only ever undoable because of it.
    /// Replaces any restore that was waiting.
    /// </summary>
    void StageFresh(string safetyBackupId);

    /// <summary>The restore or fresh start waiting for the next start, if any.</summary>
    PendingRestore? Pending();

    /// <summary>Unstages whatever is waiting for the next start. True when there was something.</summary>
    bool CancelPending();
}

/// <summary>The result of checking a bundle for restore.</summary>
/// <param name="BackupId">The bundle.</param>
/// <param name="CanRestore">True only when every check passed.</param>
/// <param name="Checks">Each check, in order, with what it found.</param>
public sealed record RestoreCheck(string BackupId, bool CanRestore, IReadOnlyList<RestoreCheckItem> Checks);

/// <summary>One check.</summary>
/// <param name="Name">What was checked, in plain words.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Detail">What was found.</param>
public sealed record RestoreCheckItem(string Name, bool Passed, string Detail);

/// <summary>A restore, or a fresh start, staged for the next start.</summary>
/// <param name="BackupId">The bundle to restore; for a fresh start, the backup of the state being set aside.</param>
/// <param name="StagedAtUtc">When it was staged.</param>
/// <param name="Kind"><c>restore</c> or <c>fresh</c>.</param>
public sealed record PendingRestore(string BackupId, DateTimeOffset StagedAtUtc, string Kind = PendingRestore.RestoreKind)
{
    /// <summary>Kind: a backup replaces the database.</summary>
    public const string RestoreKind = "restore";

    /// <summary>Kind: the database is set aside and an empty one is made.</summary>
    public const string FreshKind = "fresh";
}
