namespace ServiceHub.Core.Models.Backup;

/// <summary>
/// Describes one completed backup bundle: a timestamped directory containing a consistent
/// SQLite snapshot, an independent copy of the namespace JSON store, and this manifest
/// (persisted alongside them as <c>manifest.json</c>).
/// </summary>
/// <remarks>
/// The SQLite snapshot and the namespace JSON copy are captured independently, not as a single
/// atomic transaction across both stores — see <see cref="ConsistencyNote"/>. The SQLite file
/// has a well-defined consistent snapshot point (the instant <c>VACUUM INTO</c> completes); the
/// namespace JSON file is copied atomically as a single file but at a different, nearby instant.
/// </remarks>
public sealed record BackupManifest
{
    /// <summary>Unique, sortable identifier for this backup — the timestamped bundle directory name.</summary>
    public required string BackupId { get; init; }

    /// <summary>When this backup was created, in UTC.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>ServiceHub assembly version that produced this backup.</summary>
    public required string ServiceHubVersion { get; init; }

    /// <summary>The SQLite snapshot file (always present).</summary>
    public required BackupFileInfo Sqlite { get; init; }

    /// <summary>
    /// The namespace JSON store copy, or null if no namespace store file existed at backup time
    /// (e.g. a fresh instance with no namespaces created yet).
    /// </summary>
    public BackupFileInfo? NamespaceStore { get; init; }

    /// <summary>Result of <c>PRAGMA integrity_check</c> run against the snapshot: "ok" or the failure detail.</summary>
    public required string IntegrityCheck { get; init; }

    /// <summary>
    /// Non-reversible fingerprint of the currently active connection-string encryption key
    /// (never the key material itself). Lets an operator verify, before restoring, that the
    /// environment they're restoring into has the same encryption key as the one that produced
    /// this backup — encrypted connection strings in the namespace store will only decrypt with
    /// a matching key.
    /// </summary>
    public required string EncryptionKeyFingerprint { get; init; }

    /// <summary>
    /// Explains the two-store consistency model: the SQLite snapshot and the namespace JSON copy
    /// are each internally consistent, but were not captured as a single atomic transaction
    /// across both stores.
    /// </summary>
    public required string ConsistencyNote { get; init; }
}

/// <summary>Size and checksum of one file within a backup bundle.</summary>
public sealed record BackupFileInfo
{
    /// <summary>File name within the backup bundle directory.</summary>
    public required string FileName { get; init; }

    /// <summary>File size in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>SHA-256 checksum of the file contents, lowercase hex.</summary>
    public required string Sha256 { get; init; }
}

/// <summary>Lightweight summary of an existing backup bundle, for listing.</summary>
public sealed record BackupSummary
{
    /// <summary>Unique, sortable identifier for this backup.</summary>
    public required string BackupId { get; init; }

    /// <summary>When this backup was created, in UTC.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Combined size of all files in the bundle, in bytes.</summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>Result of the SQLite snapshot's integrity check at backup time.</summary>
    public required string IntegrityCheck { get; init; }

    /// <summary>Whether a namespace JSON store copy is present in this bundle.</summary>
    public required bool NamespaceStorePresent { get; init; }
}
