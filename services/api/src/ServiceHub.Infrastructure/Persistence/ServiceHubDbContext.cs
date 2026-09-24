using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// The ServiceHub 4.1.0 database. It grows one <see cref="DbSet{TEntity}"/> per unit that needs a
/// table (ADR-0015 D2) — never by copying 4.0.0's thirty-entity <c>DlqDbContext</c>.
/// </summary>
public sealed class ServiceHubDbContext : DbContext
{
    private readonly ResiliencePipeline _saveChangesRetryPipeline;

    /// <summary>Creates the context.</summary>
    /// <param name="options">EF Core options.</param>
    /// <param name="retryOptions">
    /// Busy/locked retry tunables; defaults apply when omitted, as they do for tests that build the
    /// context directly.
    /// </param>
    /// <param name="logger">Optional logger for retry attempts.</param>
    public ServiceHubDbContext(
        DbContextOptions<ServiceHubDbContext> options,
        SqliteBusyRetryOptions? retryOptions = null,
        ILogger<ServiceHubDbContext>? logger = null) : base(options)
    {
        _saveChangesRetryPipeline = BuildSaveChangesRetryPipeline(
            retryOptions ?? SqliteBusyRetryOptions.Default, logger);
    }

    /// <summary>Connected cloud accounts (W1).</summary>
    public DbSet<Namespace> Namespaces => Set<Namespace>();

    /// <summary>Recent activity (W1).</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureNamespace(modelBuilder);
        ConfigureAuditLog(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        _saveChangesRetryPipeline.Execute(() => base.SaveChanges(acceptAllChangesOnSuccess));

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        _saveChangesRetryPipeline
            .ExecuteAsync(
                async ct => await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct).ConfigureAwait(false),
                cancellationToken)
            .AsTask();

    // Retries SaveChanges when — and only when — the failure is SQLITE_BUSY (another connection holds
    // the write lock) or SQLITE_LOCKED. busy_timeout (SqlitePragmaConnectionInterceptor) absorbs short
    // contention inside the driver; this is the outer safety net for contention that outlasts it.
    // Never retries DbUpdateConcurrencyException or constraint violations: callers handling those
    // must see them immediately. Same behaviour as 4.0.0's DlqDbContext (roadmap F1).
    private static ResiliencePipeline BuildSaveChangesRetryPipeline(
        SqliteBusyRetryOptions retryOptions, ILogger<ServiceHubDbContext>? logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retryOptions.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(250),
                MaxDelay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransientSqliteContention),
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        "SaveChanges retry attempt {AttemptNumber} after SQLite busy/locked contention, waiting {DelayMs}ms. {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();

    private static bool IsTransientSqliteContention(Exception exception) =>
        exception switch
        {
            SqliteException sqliteException => IsBusyOrLocked(sqliteException),
            DbUpdateException { InnerException: SqliteException inner } => IsBusyOrLocked(inner),
            _ => false
        };

    // SQLITE_BUSY = 5, SQLITE_LOCKED = 6.
    private static bool IsBusyOrLocked(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6;

    // Shapes are copied from 4.0.0's DlqDbContext (ADR-0015 D3): same columns, names and indexes.
    private static void ConfigureNamespace(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Namespace>();

        entity.ToTable("Namespaces");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Name).HasMaxLength(Namespace.MaxNameLength).IsRequired();
        entity.Property(e => e.DisplayName).HasMaxLength(Namespace.MaxDisplayNameLength);
        entity.Property(e => e.Description).HasMaxLength(Namespace.MaxDescriptionLength);

        // The domain property is ConnectionString; at rest it is only ever ciphertext (ADR-0004).
        entity.Property(e => e.ConnectionString).HasColumnName("ConnectionStringEncrypted");
        entity.Property(e => e.ConnectionStringHash).HasMaxLength(64);

        entity.Property(e => e.AuthType).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(e => e.Environment).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.Provider).HasConversion<string>().HasMaxLength(16).IsRequired();

        entity.Property(e => e.AwsRegion).HasMaxLength(64);
        entity.Property(e => e.GcpProjectId).HasMaxLength(128);
        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();

        entity.HasIndex(e => new { e.OwnerId, e.Name }).IsUnique().HasDatabaseName("IX_Namespaces_OwnerId_Name");
        entity.HasIndex(e => e.OwnerId).HasDatabaseName("IX_Namespaces_OwnerId");
        entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_Namespaces_IsActive");
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLog>();

        entity.ToTable("AuditLogs");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.UserIdentity).HasMaxLength(256).IsRequired();
        entity.Property(e => e.Action).HasMaxLength(128).IsRequired();
        entity.Property(e => e.Outcome).HasMaxLength(32).IsRequired();
        entity.Property(e => e.NamespaceName).HasMaxLength(256);
        entity.Property(e => e.EntityName).HasMaxLength(512);
        entity.Property(e => e.CloudProvider).HasMaxLength(32);
        entity.Property(e => e.Environment).HasMaxLength(32);
        entity.Property(e => e.ResourceName).HasMaxLength(512);
        entity.Property(e => e.DetailsJson).HasMaxLength(8192);
        entity.Property(e => e.ErrorDetails).HasMaxLength(4096);
        entity.Property(e => e.ClientIp).HasMaxLength(64);
        entity.Property(e => e.UserAgent).HasMaxLength(512);
        entity.Property(e => e.CorrelationId).HasMaxLength(256);
        entity.Property(e => e.HttpMethod).HasMaxLength(16);
        entity.Property(e => e.HttpPath).HasMaxLength(1024);

        entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_AuditLogs_Timestamp");
        entity.HasIndex(e => new { e.OwnerId, e.Timestamp }).HasDatabaseName("IX_AuditLogs_Owner_Timestamp");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.Timestamp }).HasDatabaseName("IX_AuditLogs_Owner_Namespace_Timestamp");
        entity.HasIndex(e => e.Action).HasDatabaseName("IX_AuditLogs_Action");
    }
}
