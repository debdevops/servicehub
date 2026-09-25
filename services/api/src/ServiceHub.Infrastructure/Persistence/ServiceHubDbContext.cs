using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// The ServiceHub 4.1.0 database. It grows one <see cref="DbSet{TEntity}"/> per unit that needs a
/// table (ADR-0015 D2) — never all at once.
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

    /// <summary>Messages found in dead-letter queues (W2).</summary>
    public DbSet<DlqMessage> DlqMessages => Set<DlqMessage>();

    /// <summary>Auto Replay rules (unit 3.6).</summary>
    public DbSet<AutoReplayRule> AutoReplayRules => Set<AutoReplayRule>();

    /// <summary>Bulk replay jobs (unit 3.2).</summary>
    public DbSet<BulkOperationJob> BulkOperationJobs => Set<BulkOperationJob>();

    /// <summary>The messages of each bulk job.</summary>
    public DbSet<BulkOperationItem> BulkOperationItems => Set<BulkOperationItem>();

    /// <summary>Distinct failure fingerprints per namespace (unit 3.1).</summary>
    public DbSet<NamespaceSignature> NamespaceSignatures => Set<NamespaceSignature>();

    /// <summary>Recovery decisions — the immutable header of the evidence ledger (W2).</summary>
    public DbSet<RecoveryOperation> RecoveryOperations => Set<RecoveryOperation>();

    /// <summary>One row per recovery target — a small mutable projection over the events (W2).</summary>
    public DbSet<RecoveryLedgerEntry> RecoveryLedgerEntries => Set<RecoveryLedgerEntry>();

    /// <summary>The append-only, hash-chained evidence itself (W2).</summary>
    public DbSet<RecoveryEvent> RecoveryEvents => Set<RecoveryEvent>();

    /// <summary>What was replayed, when and by whom (W2, unit 2.7).</summary>
    public DbSet<ReplayHistory> ReplayHistories => Set<ReplayHistory>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureNamespace(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureDlqMessage(modelBuilder);
        ConfigureNamespaceSignature(modelBuilder);
        ConfigureBulkOperations(modelBuilder);
        ConfigureAutoReplayRule(modelBuilder);
        ConfigureRecoveryOperation(modelBuilder);
        ConfigureRecoveryLedgerEntry(modelBuilder);
        ConfigureRecoveryEvent(modelBuilder);
        ConfigureReplayHistory(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        return _saveChangesRetryPipeline.Execute(() => base.SaveChanges(acceptAllChangesOnSuccess));
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Append-only is enforced here, beneath every caller — a hand-written save cannot bypass it.
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        return _saveChangesRetryPipeline
            .ExecuteAsync(
                async ct => await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct).ConfigureAwait(false),
                cancellationToken)
            .AsTask();
    }

    // Retries SaveChanges when — and only when — the failure is SQLITE_BUSY (another connection holds
    // the write lock) or SQLITE_LOCKED. busy_timeout (SqlitePragmaConnectionInterceptor) absorbs short
    // contention inside the driver; this is the outer safety net for contention that outlasts it.
    // Never retries DbUpdateConcurrencyException or constraint violations: callers handling those
    // must see them immediately. 
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

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, string> SortableUtc =
        new(
            v => v.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, string?> SortableUtcNullable =
        new(
            v => v.HasValue ? v.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : null,
            v => v == null ? null : DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));

    private static void ConfigureDlqMessage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DlqMessage>();

        entity.ToTable("DlqMessages");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.MessageId).HasMaxLength(256).IsRequired();
        entity.Property(e => e.BodyHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.EntityName).HasMaxLength(512).IsRequired();
        entity.Property(e => e.TopicName).HasMaxLength(512);
        entity.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.CloudProvider).HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.DeadLetterReason).HasMaxLength(1024);
        entity.Property(e => e.DeadLetterErrorDescription).HasMaxLength(4096);
        entity.Property(e => e.ContentType).HasMaxLength(256);
        entity.Property(e => e.BodyPreview).HasMaxLength(2048);
        entity.Property(e => e.ApplicationPropertiesJson).HasMaxLength(8192);
        entity.Property(e => e.CorrelationId).HasMaxLength(256);
        entity.Property(e => e.SessionId).HasMaxLength(256);
        entity.Property(e => e.SignatureHash).HasMaxLength(64);
        entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();

        // Sortable UTC text, as for the audit trail: SQLite cannot ORDER BY its default
        // DateTimeOffset encoding, and this table is read newest-first.
        entity.Property(e => e.EnqueuedTimeUtc).HasConversion(SortableUtc);
        entity.Property(e => e.DetectedAtUtc).HasConversion(SortableUtc);
        entity.Property(e => e.ResolvedAt).HasConversion(SortableUtcNullable);
        entity.Property(e => e.ArchivedAt).HasConversion(SortableUtcNullable);
        entity.Property(e => e.ResolutionCause).HasConversion<string>().HasMaxLength(32);

        // The same message in the same place is one row (per owner).
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.EntityName, e.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("IX_DlqMessages_Owner_Namespace_Entity_Sequence");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.Status }).HasDatabaseName("IX_DlqMessages_Owner_Namespace_Status");
        entity.HasIndex(e => e.BodyHash).HasDatabaseName("IX_DlqMessages_BodyHash");
        entity.HasIndex(e => e.DetectedAtUtc).HasDatabaseName("IX_DlqMessages_DetectedAt");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash }).HasDatabaseName("IX_DlqMessages_Owner_Namespace_Signature");
    }

    private static void ConfigureAutoReplayRule(ModelBuilder modelBuilder)
    {
        var rule = modelBuilder.Entity<AutoReplayRule>();
        rule.ToTable("AutoReplayRules");
        rule.HasKey(e => e.Id);
        rule.Property(e => e.Id).ValueGeneratedOnAdd();
        rule.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        rule.Property(e => e.Name).HasMaxLength(120).IsRequired();
        rule.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
        rule.Property(e => e.Reason).HasMaxLength(1024);
        rule.Property(e => e.EntityName).HasMaxLength(512);
        rule.Property(e => e.SignatureHash).HasMaxLength(64);
        rule.Property(e => e.DisabledReason).HasMaxLength(32);
        rule.Property(e => e.DisabledDetail).HasMaxLength(512);
        rule.Property(e => e.LastAskedReason).HasMaxLength(128);
        rule.Property(e => e.CreatedAt).HasConversion(SortableUtc);
        rule.Property(e => e.UpdatedAt).HasConversion(SortableUtcNullable);
        rule.Property(e => e.LastAskedAt).HasConversion(SortableUtcNullable);
        rule.HasIndex(e => new { e.OwnerId, e.Provider, e.Enabled }).HasDatabaseName("IX_AutoReplayRules_Owner_Provider_Enabled");
    }

    private static void ConfigureBulkOperations(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<BulkOperationJob>();
        job.ToTable("BulkOperationJobs");
        job.HasKey(e => e.Id);
        job.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        job.Property(e => e.ActorIdentity).HasMaxLength(256).IsRequired();
        job.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(16);
        job.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        job.Property(e => e.EndedReason).HasMaxLength(1024);
        job.Property(e => e.PreviewedAt).HasConversion(SortableUtc);
        job.Property(e => e.StartedAt).HasConversion(SortableUtcNullable);
        job.Property(e => e.EndedAt).HasConversion(SortableUtcNullable);
        job.HasIndex(e => new { e.OwnerId, e.Status }).HasDatabaseName("IX_BulkOperationJobs_Owner_Status");
        job.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.JobId).OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<BulkOperationItem>();
        item.ToTable("BulkOperationItems");
        item.HasKey(e => e.Id);
        item.Property(e => e.Id).ValueGeneratedOnAdd();
        item.Property(e => e.EntityName).HasMaxLength(512).IsRequired();
        item.Property(e => e.DeadLetterReason).HasMaxLength(1024);
        item.Property(e => e.State).HasConversion<string>().HasMaxLength(16);
        item.Property(e => e.ReasonCode).HasMaxLength(128);
        item.Property(e => e.Remedy).HasMaxLength(512);
        item.HasIndex(e => new { e.JobId, e.Position }).HasDatabaseName("IX_BulkOperationItems_Job_Position");
    }

    private static void ConfigureNamespaceSignature(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NamespaceSignature>();

        entity.ToTable("NamespaceSignatures");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();
        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.SignatureHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.DominantDeadletterReason).HasMaxLength(1024).IsRequired();
        entity.Property(e => e.EntityName).HasMaxLength(512).IsRequired();
        entity.Property(e => e.ExampleError).HasMaxLength(1024);
        entity.Property(e => e.TopTermsJson).HasMaxLength(2048).IsRequired();
        entity.Property(e => e.FirstSeenAt).HasConversion(SortableUtc);
        entity.Property(e => e.LastSeenAt).HasConversion(SortableUtc);

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash }).IsUnique()
            .HasDatabaseName("IX_NamespaceSignatures_Owner_Namespace_Hash");
    }

    private static void ConfigureRecoveryOperation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecoveryOperation>();

        entity.ToTable("RecoveryOperations");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(e => e.ActorIdentity).HasMaxLength(256).IsRequired();
        entity.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.ActorScopes).HasMaxLength(1024);
        entity.Property(e => e.Reason).HasMaxLength(2048);
        entity.Property(e => e.IntentHeader).HasMaxLength(128);
        entity.Property(e => e.NamespaceNameSnapshot).HasMaxLength(256);
        entity.Property(e => e.ProviderSnapshot).HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.EnvironmentSnapshot).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.ScopeDescription).HasMaxLength(1024).IsRequired();
        entity.Property(e => e.CorrelationId).HasMaxLength(256);
        entity.Property(e => e.ServiceVersion).HasMaxLength(32).IsRequired();
        entity.Property(e => e.OpenedAt).HasConversion(SortableUtc);

        entity.HasIndex(e => new { e.OwnerId, e.OpenedAt }).HasDatabaseName("IX_RecoveryOperations_Owner_OpenedAt");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.OpenedAt }).HasDatabaseName("IX_RecoveryOperations_Owner_Namespace_OpenedAt");
    }

    private static void ConfigureRecoveryLedgerEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecoveryLedgerEntry>();

        entity.ToTable("RecoveryLedgerEntries");
        entity.HasKey(e => e.Id);

        // No foreign keys, deliberately: deleting a DlqMessage or a namespace can never reach the ledger.
        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.NamespaceNameSnapshot).HasMaxLength(256);
        entity.Property(e => e.ProviderSnapshot).HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.EnvironmentSnapshot).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.EntityNameSnapshot).HasMaxLength(512);
        entity.Property(e => e.EntityTypeSnapshot).HasMaxLength(32);
        entity.Property(e => e.TopicNameSnapshot).HasMaxLength(512);
        entity.Property(e => e.SourceMessageIdSnapshot).HasMaxLength(256);
        entity.Property(e => e.BodyHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.FailureCategorySnapshot).HasConversion<string>().HasMaxLength(32);
        entity.Property(e => e.DeadLetterReasonSnapshot).HasMaxLength(1024);
        entity.Property(e => e.SignatureHashSnapshot).HasMaxLength(64);
        entity.Property(e => e.RecoveryMarker).HasMaxLength(64);
        entity.Property(e => e.TargetEntity).HasMaxLength(512).IsRequired();
        entity.Property(e => e.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(e => e.Disposition).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.VerificationResult).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.VerificationConfidence).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.BegunAt).HasConversion(SortableUtc);
        entity.Property(e => e.ObservationWindowEndsAt).HasConversion(SortableUtcNullable);
        entity.Property(e => e.ClosedAt).HasConversion(SortableUtcNullable);

        entity.HasIndex(e => new { e.OwnerId, e.State, e.BegunAt }).HasDatabaseName("IX_RecoveryLedgerEntries_Owner_State_BegunAt");
        entity.HasIndex(e => e.OperationId).HasDatabaseName("IX_RecoveryLedgerEntries_OperationId");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.EntityNameSnapshot, e.BodyHash })
            .HasDatabaseName("IX_RecoveryLedgerEntries_Owner_Namespace_Entity_BodyHash");
        entity.HasIndex(e => e.RecoveryMarker)
            .IsUnique()
            .HasFilter("[RecoveryMarker] IS NOT NULL")
            .HasDatabaseName("IX_RecoveryLedgerEntries_RecoveryMarker");
    }

    private static void ConfigureRecoveryEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecoveryEvent>();

        entity.ToTable("RecoveryEvents");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(e => e.ActorIdentity).HasMaxLength(256).IsRequired();
        entity.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.DetailJson).HasMaxLength(8192);
        entity.Property(e => e.PrevHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.EntryHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.OccurredAt).HasConversion(SortableUtc);

        entity.HasIndex(e => new { e.OwnerId, e.Seq }).IsUnique().HasDatabaseName("IX_RecoveryEvents_Owner_Seq");
        entity.HasIndex(e => new { e.EntryId, e.Seq }).HasDatabaseName("IX_RecoveryEvents_EntryId_Seq");
        entity.HasIndex(e => new { e.OperationId, e.Seq }).HasDatabaseName("IX_RecoveryEvents_OperationId_Seq");
        entity.HasIndex(e => new { e.OwnerId, e.EventType, e.Seq }).HasDatabaseName("IX_RecoveryEvents_Owner_EventType_Seq");
    }

    private static void ConfigureReplayHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReplayHistory>();

        entity.ToTable("ReplayHistories");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        // Soft references only: a history of what was done outlives the message it was done to.
        entity.Property(e => e.OwnerId).HasMaxLength(128).IsRequired();
        entity.Property(e => e.MessageId).HasMaxLength(256).IsRequired();
        entity.Property(e => e.SourceEntity).HasMaxLength(512).IsRequired();
        entity.Property(e => e.ReplayedBy).HasMaxLength(256).IsRequired();
        entity.Property(e => e.ReplayStrategy).HasMaxLength(64).IsRequired();
        entity.Property(e => e.ReplayedToEntity).HasMaxLength(512).IsRequired();
        entity.Property(e => e.OutcomeStatus).HasMaxLength(32).IsRequired();
        entity.Property(e => e.NewDeadLetterReason).HasMaxLength(1024);
        entity.Property(e => e.ErrorDetails).HasMaxLength(4096);
        entity.Property(e => e.ReplayedAt).HasConversion(SortableUtc);

        entity.HasIndex(e => e.DlqMessageId).HasDatabaseName("IX_ReplayHistories_DlqMessageId");
        entity.HasIndex(e => e.ReplayedAt).HasDatabaseName("IX_ReplayHistories_ReplayedAt");
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.ReplayedAt }).HasDatabaseName("IX_ReplayHistories_Owner_Namespace_ReplayedAt");
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLog>();

        entity.ToTable("AuditLogs");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        // Stored as sortable UTC text (same TEXT column). SQLite cannot ORDER BY its default
        // DateTimeOffset encoding, and the audit trail is read newest-first.
        entity.Property(e => e.Timestamp).HasConversion(
            v => v.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));

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
