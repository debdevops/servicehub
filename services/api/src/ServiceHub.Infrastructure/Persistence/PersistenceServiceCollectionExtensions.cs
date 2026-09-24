using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>Registers the database, its single-instance lock and its readiness check.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Health-check tag that puts a check on <c>/health/ready</c> and off <c>/health</c>.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>Default busy timeout; overridable with <c>ServiceHub:BusyTimeoutMilliseconds</c>.</summary>
    private const string BusyTimeoutKey = "ServiceHub:BusyTimeoutMilliseconds";
    private const string MaxBusyRetryKey = "ServiceHub:MaxBusyRetryAttempts";

    /// <summary>Adds <see cref="ServiceHubDbContext"/>, <see cref="SqliteInstanceLock"/> and the SQLite readiness check.</summary>
    public static IServiceCollection AddServiceHubPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<ServiceHubDbContext>((serviceProvider, options) =>
        {
            // Resolved from the built container, not captured at registration time: a test host's
            // configuration overrides are only layered in after registration, and reading them
            // early would send concurrent hosts to the same default file.
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var dataDirectory = ServiceHubDataDirectory.Resolve(configuration);
            Directory.CreateDirectory(dataDirectory);

            var busyTimeoutMilliseconds = configuration.GetValue(
                BusyTimeoutKey, SqlitePragmaConnectionInterceptor.DefaultBusyTimeoutMilliseconds);

            // Microsoft.Data.Sqlite retries BUSY/LOCKED on its own schedule, bounded by
            // CommandTimeout — not by the busy_timeout PRAGMA. Left at EF's 30 s default the
            // configured value would be cosmetic, so the two are set together.
            var commandTimeoutSeconds = (int)Math.Ceiling(busyTimeoutMilliseconds / 1000.0);
            options.UseSqlite(
                $"Data Source={ServiceHubDataDirectory.ResolveDatabasePath(configuration)}",
                sqlite => sqlite.CommandTimeout(commandTimeoutSeconds));
            options.AddInterceptors(
                new SqlitePragmaConnectionInterceptor(busyTimeoutMilliseconds),
                new ConnectionStringEncryptionInterceptor(serviceProvider.GetRequiredService<IConnectionStringProtector>()));

            if (serviceProvider.GetService<IHostEnvironment>()?.IsDevelopment() == true)
            {
                options.EnableDetailedErrors();
            }
        });

        // One protector for the process: it owns the key registry, which is validated once at
        // startup (a bad registry is fatal then, never on first use).
        services.TryAddSingleton<IConnectionStringProtector, ConnectionStringProtector>();

        services.TryAddScoped<INamespaceRepository, NamespaceRepository>();

        // Single-instance invariant (ADR-0003). Singleton, so the OS file lock is held for the
        // process lifetime and released by the container on shutdown.
        services.TryAddSingleton(serviceProvider =>
            new SqliteInstanceLock(serviceProvider.GetRequiredService<IConfiguration>()));

        services.TryAddSingleton(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new SqliteBusyRetryOptions
            {
                MaxRetryAttempts = configuration.GetValue(MaxBusyRetryKey, SqliteBusyRetryOptions.Default.MaxRetryAttempts)
            };
        });

        services.AddHealthChecks()
            .AddCheck<SqliteDatabaseHealthCheck>("sqlite", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// Takes the instance lock, then applies migrations. Call once at startup, before serving.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Another instance holds the data directory, or the file is not a ServiceHub 4.1.0 database.
    /// </exception>
    public static async Task InitializeServiceHubDatabaseAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Eager on purpose: a second instance must fail here, before any database access.
        _ = services.GetRequiredService<SqliteInstanceLock>();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceHub.Persistence");
        var dbPath = ServiceHubDataDirectory.ResolveDatabasePath(services.GetRequiredService<IConfiguration>());

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();

        await EnsureSchemaIsRecognisedAsync(db, dbPath, cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("ServiceHub database ready at {DatabasePath}", dbPath);
    }

    // ADR-0015 D4: ServiceHub only opens a database it created itself, and does not try to be clever about it. A file
    // that already holds tables ServiceHub did not migrate itself, or a migration this build has
    // never heard of, means "not ours" — refuse, in words that say what to do.
    private static async Task EnsureSchemaIsRecognisedAsync(
        ServiceHubDbContext db, string dbPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
        {
            return;
        }

        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var unknown = applied.Where(id => !known.Contains(id)).ToList();

        if (unknown.Count > 0)
        {
            throw NotRecognised(dbPath, $"it records migration '{unknown[0]}', which this version does not have");
        }

        if (applied.Count == 0 && await HasUserTablesAsync(db, cancellationToken).ConfigureAwait(false))
        {
            throw NotRecognised(dbPath, "it already contains tables that ServiceHub did not create");
        }
    }

    private static async Task<bool> HasUserTablesAsync(ServiceHubDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
            var count = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
            return count > 0;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static InvalidOperationException NotRecognised(string dbPath, string reason) =>
        new(
            $"The database at '{dbPath}' is not a ServiceHub 4.1.0 database: {reason}. " +
            "ServiceHub starts from a fresh schema and cannot open a database created by another application or version (ADR-0015). " +
            "Point ServiceHub:DataDirectory at an empty directory to start clean.");
}
