using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Applies SQLite hardening PRAGMAs on every new connection: WAL journaling so readers never
/// block a writer (or vice versa), and a <c>busy_timeout</c> so a writer contending with
/// another connection's write lock blocks and retries inside the SQLite driver instead of
/// failing immediately with SQLITE_BUSY. Roadmap F1 — eight independent background workers
/// write to a single SQLite file with neither of these configured today.
/// </summary>
/// <remarks>
/// <c>busy_timeout</c> alone does not bound how long a caller actually waits: Microsoft.Data.Sqlite
/// retries SQLITE_BUSY/SQLITE_LOCKED internally on its own schedule, gated by
/// <see cref="Microsoft.Data.Sqlite.SqliteCommand.CommandTimeout"/> (30s by default), not by this
/// PRAGMA. Callers that configure <see cref="_busyTimeoutMilliseconds"/> must also set
/// <c>CommandTimeout</c> to match (see <c>DependencyInjection.AddDlqDatabase</c>) or the
/// configured value has no real effect on contention wait time.
/// </remarks>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>Default busy_timeout applied when <c>DlqDatabase:BusyTimeoutMilliseconds</c> is
    /// not configured — generous enough to absorb ordinary write contention between the eight
    /// background workers without masking genuinely stuck locks.</summary>
    public const int DefaultBusyTimeoutMilliseconds = 5000;

    private readonly int _busyTimeoutMilliseconds;

    public SqlitePragmaConnectionInterceptor(int busyTimeoutMilliseconds)
    {
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds;
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();

        TryApplyWalJournalMode(command);

        command.CommandText = $"PRAGMA busy_timeout={_busyTimeoutMilliseconds};";
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        await TryApplyWalJournalModeAsync(command, cancellationToken).ConfigureAwait(false);

        command.CommandText = $"PRAGMA busy_timeout={_busyTimeoutMilliseconds};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // EF Core's SqliteDatabaseCreator briefly opens a read-only connection purely to check
    // whether the database file already exists, before migrations run. WAL mode requires write
    // access to create the -wal/-shm files, so that connection can never apply it — and doesn't
    // need to, since it's discarded immediately after the check; the real read-write connection
    // EF opens next fires ConnectionOpened again and succeeds. Any other failure is unexpected
    // and must still surface.
    private static void TryApplyWalJournalMode(DbCommand command)
    {
        try
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8) // SQLITE_READONLY
        {
        }
    }

    private static async Task TryApplyWalJournalModeAsync(DbCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8) // SQLITE_READONLY
        {
        }
    }
}
