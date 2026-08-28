using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Roadmap F1 (SQLite hardening): proves the interceptor actually applies WAL journaling and
/// the configured busy_timeout, not just that it compiles. WAL requires a real file-backed
/// database — SQLite always reports "memory" journal mode for <c>:memory:</c> and shared-cache
/// in-memory databases regardless of what is PRAGMA'd, so this test cannot use the ":memory:"
/// pattern the rest of this test project relies on.
/// </summary>
public sealed class SqlitePragmaConnectionInterceptorTests : IDisposable
{
    private readonly string _dbPath;

    public SqlitePragmaConnectionInterceptorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"servicehub-pragma-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ConnectionOpened_SetsWalJournalModeAndConfiguredBusyTimeout()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeoutMilliseconds: 4321))
            .Options;

        using var dbContext = new DlqDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await dbContext.Database.OpenConnectionAsync();

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();

        using var journalModeCommand = connection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode;";
        var journalMode = (string)(await journalModeCommand.ExecuteScalarAsync())!;
        journalMode.Should().BeEquivalentTo("wal");

        using var busyTimeoutCommand = connection.CreateCommand();
        busyTimeoutCommand.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(await busyTimeoutCommand.ExecuteScalarAsync());
        busyTimeout.Should().Be(4321);

        await dbContext.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task ConnectionOpened_ReappliesPragmasOnEveryNewConnection()
    {
        // busy_timeout is a per-connection SQLite session setting, not a database-file
        // property like journal_mode — it must be re-applied every time a new connection is
        // opened, not just once. This opens and closes the underlying connection twice against
        // the same file to confirm the second open still carries the configured value.
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeoutMilliseconds: 777))
            .Options;

        using var dbContext = new DlqDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.CloseConnectionAsync();
        await dbContext.Database.OpenConnectionAsync();

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        using var busyTimeoutCommand = connection.CreateCommand();
        busyTimeoutCommand.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(await busyTimeoutCommand.ExecuteScalarAsync());
        busyTimeout.Should().Be(777);

        await dbContext.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task ConnectionOpened_SqliteReadonlyOnWalPragma_IsSwallowed_ConnectionStaysUsable()
    {
        // Reproduces the scenario that previously broke EF Core startup/migrations:
        // SqliteDatabaseCreator.Exists() opens a genuinely read-only ADO.NET connection against
        // an already-existing file purely to check for the file's presence. WAL requires write
        // access to create the -wal/-shm files, so "PRAGMA journal_mode=WAL;" on that connection
        // must fail with SQLITE_READONLY (8) whenever the on-disk mode isn't already WAL — and
        // the interceptor must swallow exactly that, not propagate it and break startup.
        //
        // EF Core's own SqliteDatabaseCreator sets journal_mode=WAL as part of EnsureCreated, so
        // the file must be forced back to a non-WAL mode afterwards to reproduce the failing
        // transition this test targets (mirrors an existing file created before WAL hardening
        // shipped, or one produced by a non-EF tool).
        var createOptions = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using (var createContext = new DlqDbContext(createOptions))
        {
            await createContext.Database.EnsureCreatedAsync();
            await createContext.Database.OpenConnectionAsync();
            using var revertJournalModeCommand = createContext.Database.GetDbConnection().CreateCommand();
            revertJournalModeCommand.CommandText = "PRAGMA journal_mode=DELETE;";
            await revertJournalModeCommand.ExecuteNonQueryAsync();
            await createContext.Database.CloseConnectionAsync();
        }

        SqliteConnection.ClearAllPools();

        var readOnlyConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(readOnlyConnectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeoutMilliseconds: 999))
            .Options;

        using var dbContext = new DlqDbContext(options);

        var openAct = async () => await dbContext.Database.OpenConnectionAsync();
        await openAct.Should().NotThrowAsync();

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();

        // journal_mode was NOT switched to WAL — the read-only connection can't write the
        // -wal/-shm files, so the on-disk (non-WAL) mode is left untouched.
        using var journalModeCommand = connection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode;";
        var journalMode = (string)(await journalModeCommand.ExecuteScalarAsync())!;
        journalMode.Should().NotBeEquivalentTo("wal");

        // busy_timeout still gets applied unconditionally — it comes after the WAL attempt and
        // doesn't require write access.
        using var busyTimeoutCommand = connection.CreateCommand();
        busyTimeoutCommand.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(await busyTimeoutCommand.ExecuteScalarAsync());
        busyTimeout.Should().Be(999);

        // The connection is left fully usable for normal reads after the swallowed failure.
        var queryAct = async () => await dbContext.DlqMessages.CountAsync();
        await queryAct.Should().NotThrowAsync();

        await dbContext.Database.CloseConnectionAsync();
    }
}
