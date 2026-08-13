namespace ServiceHub.Core.Enums;

/// <summary>
/// A report-friendly terminal outcome category for a <see cref="Entities.RecoveryLedgerEntry"/>.
/// Set once, atomically with the corresponding terminal <see cref="RecoveryEntryState"/> — null
/// while the entry is still non-terminal. Exists alongside <see cref="RecoveryEntryState"/> so
/// rollups (e.g. "n Recovered / n Returned / n Unverified / n Failed") don't need to re-derive
/// terminality from the finer-grained state on every read.
/// </summary>
public enum RecoveryDisposition
{
    /// <summary>Mirrors <see cref="RecoveryEntryState.Recovered"/>.</summary>
    Recovered = 0,

    /// <summary>Mirrors <see cref="RecoveryEntryState.Returned"/>.</summary>
    Returned = 1,

    /// <summary>Mirrors <see cref="RecoveryEntryState.ExecutionFailed"/>.</summary>
    Failed = 2,

    /// <summary>Mirrors <see cref="RecoveryEntryState.Discarded"/>.</summary>
    Discarded = 3,

    /// <summary>Mirrors <see cref="RecoveryEntryState.Unverified"/>.</summary>
    Unverified = 4,

    /// <summary>Mirrors <see cref="RecoveryEntryState.WrittenOff"/>.</summary>
    WrittenOff = 5,

    /// <summary>Mirrors <see cref="RecoveryEntryState.Expired"/>. Reserved, unused this phase.</summary>
    Expired = 6
}
