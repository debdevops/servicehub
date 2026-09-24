using Microsoft.Extensions.Configuration;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Enforces the single-writer assumption <see cref="RecoveryLedger.RecoveryLedgerService"/>'s
/// hash chain depends on. The chain is sequenced with an in-process,
/// per-owner <c>SemaphoreSlim</c> — a real guarantee inside one process, but nothing
/// stopped a second process from opening the same SQLite file and silently corrupting the chain.
/// Taking an OS-level exclusive lock on a marker file in the data directory turns that unguarded
/// assumption into an enforced invariant: a second instance against the same directory fails
/// fast at startup instead of running.
/// </summary>
public sealed class SqliteInstanceLock : IDisposable
{
    private const string LockFileName = ".instance.lock";

    private readonly FileStream _lockStream;

    /// <summary>Takes the lock on the directory <see cref="ServiceHubDataDirectory"/> resolves.</summary>
    public SqliteInstanceLock(IConfiguration configuration)
    {
        var dataDir = ServiceHubDataDirectory.Resolve(configuration);
        Directory.CreateDirectory(dataDir);

        var lockPath = Path.Combine(dataDir, LockFileName);
        try
        {
            // FileShare.None is the lock itself: a second process opening the same path in any
            // sharing mode fails with an IOException, on both Windows and Unix. Held for the
            // process lifetime via this field; released automatically when the handle closes on
            // process exit (including a crash), so there is no stale-lock file to clean up.
            _lockStream = new FileStream(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Another ServiceHub instance already holds the data directory '{dataDir}'. " +
                "The recovery evidence ledger's hash chain assumes a single writer process; a " +
                "second instance against the same SQLite file would corrupt it silently. Stop " +
                "the other instance, or point this one at a different ServiceHub:DataDirectory.",
                ex);
        }
    }

    public void Dispose() => _lockStream.Dispose();
}
