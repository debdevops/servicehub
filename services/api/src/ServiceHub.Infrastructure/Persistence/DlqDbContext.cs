using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for DLQ Intelligence data.
/// Uses SQLite for lightweight, file-based persistence.
/// </summary>
public sealed class DlqDbContext : DbContext
{
    private readonly ResiliencePipeline _saveChangesRetryPipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqDbContext"/> class.
    /// </summary>
    /// <param name="options">The EF Core context options.</param>
    /// <param name="retryOptions">Busy/locked retry tunables for <see cref="SaveChanges"/> and
    /// <see cref="SaveChangesAsync(bool, CancellationToken)"/>. Optional and resolved from DI in
    /// production; defaults apply when omitted, as every existing test fixture that constructs
    /// this type directly with only <paramref name="options"/> does.</param>
    /// <param name="logger">Optional logger for retry attempts; silent when not supplied.</param>
    public DlqDbContext(
        DbContextOptions<DlqDbContext> options,
        SqliteBusyRetryOptions? retryOptions = null,
        ILogger<DlqDbContext>? logger = null) : base(options)
    {
        _saveChangesRetryPipeline = BuildSaveChangesRetryPipeline(retryOptions ?? SqliteBusyRetryOptions.Default, logger);
    }

    /// <summary>Dead-letter queue messages.</summary>
    public DbSet<DlqMessage> DlqMessages => Set<DlqMessage>();

    /// <summary>Replay history entries.</summary>
    public DbSet<ReplayHistory> ReplayHistories => Set<ReplayHistory>();

    /// <summary>Auto-replay rules.</summary>
    public DbSet<AutoReplayRule> AutoReplayRules => Set<AutoReplayRule>();

    /// <summary>Persistent audit trail entries.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Bulk replay/purge operation jobs.</summary>
    public DbSet<BulkOperationJob> BulkOperationJobs => Set<BulkOperationJob>();

    /// <summary>Structured feature snapshots captured per DLQ message at scan time.</summary>
    public DbSet<MessageFeatureRecord> MessageFeatureRecords => Set<MessageFeatureRecord>();

    /// <summary>Tracks whether a DLQ error cluster's signature has been seen before in a namespace.</summary>
    public DbSet<NamespaceSignature> NamespaceSignatures => Set<NamespaceSignature>();

    /// <summary>Operational knowledge associated with FailureSignatures.</summary>
    public DbSet<FailureKnowledgeEntity> FailureKnowledgeEntities => Set<FailureKnowledgeEntity>();

    /// <summary>Prior versions of operational knowledge, snapshotted before each edit.</summary>
    public DbSet<FailureKnowledgeHistoryEntity> FailureKnowledgeHistoryEntities => Set<FailureKnowledgeHistoryEntity>();

    /// <summary>Durable current lifecycle status (Active/Resolved/Reopened/Suppressed/Archived) per failure signature.</summary>
    public DbSet<SignatureLifecycleState> SignatureLifecycleStates => Set<SignatureLifecycleState>();

    /// <summary>Append-only transition history for failure signature lifecycle changes.</summary>
    public DbSet<SignatureLifecycleHistoryEntry> SignatureLifecycleHistory => Set<SignatureLifecycleHistoryEntry>();

    /// <summary>Signature-replay operation jobs.</summary>
    public DbSet<SignatureReplayJob> SignatureReplayJobs => Set<SignatureReplayJob>();

    /// <summary>Recovery Evidence Ledger: immutable headers, one per operator/automation decision.</summary>
    public DbSet<RecoveryOperation> RecoveryOperations => Set<RecoveryOperation>();

    /// <summary>Recovery Evidence Ledger: one row per (operation, message), mostly immutable
    /// with a small mutable lifecycle projection.</summary>
    public DbSet<RecoveryLedgerEntry> RecoveryLedgerEntries => Set<RecoveryLedgerEntry>();

    /// <summary>Recovery Evidence Ledger: append-only, hash-chained events — the evidence itself.</summary>
    public DbSet<RecoveryEvent> RecoveryEvents => Set<RecoveryEvent>();

    /// <summary>Current earned-autonomy projection per (owner, signature, action) — the sole
    /// mutable table in the Recovery Evidence Ledger family. See <see cref="AutonomyGrant"/>.</summary>
    public DbSet<AutonomyGrant> AutonomyGrants => Set<AutonomyGrant>();

    /// <summary>Namespace connection registrations — replaces the JSON-file-backed store (M2).</summary>
    public DbSet<Core.Entities.Namespace> Namespaces => Set<Core.Entities.Namespace>();

    /// <summary>Durable form of <see cref="Core.Entities.Namespace.SharedWithOwnerIds"/> — one row
    /// per (namespace, shared-with-owner) pair (M2).</summary>
    public DbSet<NamespaceSharedOwner> NamespaceSharedOwners => Set<NamespaceSharedOwner>();

    /// <summary>Governance/RBAC grants — per-owner, per-namespace, per-pillar access model (M3).
    /// Deliberately not hash-chained; see <see cref="GovernanceGrant"/>.</summary>
    public DbSet<GovernanceGrant> GovernanceGrants => Set<GovernanceGrant>();

    /// <summary>Playbook Ledger: immutable identity/context, one row per proposal, with a small
    /// mutable lifecycle projection (M4). See <see cref="PlaybookEntry"/>.</summary>
    public DbSet<PlaybookEntry> PlaybookEntries => Set<PlaybookEntry>();

    /// <summary>Playbook Ledger: append-only, hash-chained events — the evidence itself, on a
    /// fully independent chain from <see cref="RecoveryEvents"/> (M4).</summary>
    public DbSet<PlaybookEvent> PlaybookEvents => Set<PlaybookEvent>();

    /// <summary>C3's raw input: durably recorded deploy/config-change signals (M5, ADR-0008). No
    /// hash chain — see <see cref="ExternalSignalEvent"/>.</summary>
    public DbSet<ExternalSignalEvent> ExternalSignalEvents => Set<ExternalSignalEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplyUtcDateTimeConverters(modelBuilder);

        ConfigureDlqMessage(modelBuilder);
        ConfigureReplayHistory(modelBuilder);
        ConfigureAutoReplayRule(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureBulkOperationJob(modelBuilder);
        ConfigureMessageFeatureRecord(modelBuilder);
        ConfigureNamespaceSignature(modelBuilder);
        ConfigureFailureKnowledge(modelBuilder);
        ConfigureFailureKnowledgeHistory(modelBuilder);
        ConfigureSignatureLifecycleState(modelBuilder);
        ConfigureSignatureLifecycleHistoryEntry(modelBuilder);
        ConfigureSignatureReplayJob(modelBuilder);
        ConfigureRecoveryOperation(modelBuilder);
        ConfigureRecoveryLedgerEntry(modelBuilder);
        ConfigureRecoveryEvent(modelBuilder);
        ConfigureAutonomyGrant(modelBuilder);
        ConfigureNamespace(modelBuilder);
        ConfigureNamespaceSharedOwner(modelBuilder);
        ConfigureGovernanceGrant(modelBuilder);
        ConfigurePlaybookEntry(modelBuilder);
        ConfigurePlaybookEvent(modelBuilder);
        ConfigureExternalSignalEvent(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        PlaybookLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        StampAutonomyGrantConcurrencyTokens();
        return _saveChangesRetryPipeline.Execute(() => base.SaveChanges(acceptAllChangesOnSuccess));
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        PlaybookLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        StampAutonomyGrantConcurrencyTokens();
        return _saveChangesRetryPipeline
            .ExecuteAsync(
                async ct => await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct).ConfigureAwait(false),
                cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Builds the Polly pipeline that retries <see cref="SaveChanges"/>/<see cref="SaveChangesAsync(bool, CancellationToken)"/>
    /// when — and only when — the failure is SQLITE_BUSY (another connection holds the write
    /// lock) or SQLITE_LOCKED (a conflicting lock within the same connection). busy_timeout
    /// (see <see cref="SqlitePragmaConnectionInterceptor"/>) already absorbs short contention
    /// inside the SQLite driver itself; this is the outer safety net for when contention
    /// outlasts that. Deliberately never retries <see cref="DbUpdateConcurrencyException"/> or
    /// constraint-violation <see cref="DbUpdateException"/>s — every caller across the app that
    /// already catches those for optimistic-concurrency handling must keep seeing them
    /// immediately (roadmap F1).
    /// </summary>
    private static ResiliencePipeline BuildSaveChangesRetryPipeline(SqliteBusyRetryOptions retryOptions, ILogger<DlqDbContext>? logger)
    {
        return new ResiliencePipelineBuilder()
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
                        "DlqDbContext.SaveChanges retry attempt {AttemptNumber} after SQLite busy/locked contention, waiting {DelayMs}ms. {ExceptionMessage}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

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

    /// <summary>
    /// Assigns a fresh <see cref="AutonomyGrant.ConcurrencyStamp"/> to every tracked
    /// <see cref="AutonomyGrant"/> being inserted or updated, before the base save runs —
    /// structural, not a convention any caller must remember (roadmap §9.4.4 Correction).
    /// </summary>
    private void StampAutonomyGrantConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<AutonomyGrant>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.ConcurrencyStamp = Guid.NewGuid();
            }
        }
    }

    private static void ApplyUtcDateTimeConverters(ModelBuilder modelBuilder)
    {
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableDateTimeOffsetConverter);
                }
            }
        }
    }

    private static void ConfigureDlqMessage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DlqMessage>();

        entity.ToTable("DlqMessages");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MessageId)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.BodyHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.EntityName)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.EntityType)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.CloudProvider)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(Core.Enums.CloudProviderType.Azure);

        entity.Property(e => e.DeadLetterReason)
            .HasMaxLength(1024);

        entity.Property(e => e.DeadLetterErrorDescription)
            .HasMaxLength(4096);

        entity.Property(e => e.ContentType)
            .HasMaxLength(256);

        entity.Property(e => e.BodyPreview)
            .HasMaxLength(2048);

        entity.Property(e => e.ApplicationPropertiesJson)
            .HasMaxLength(8192);

        entity.Property(e => e.FailureCategory)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Concurrency token: guards against two replay workers (bulk replay, signature
        // replay, auto-replay) racing on the same row. EF adds the original Status value to
        // the WHERE clause of every UPDATE/DELETE, so a losing writer gets
        // DbUpdateConcurrencyException instead of silently overwriting the winner's write.
        entity.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsConcurrencyToken();

        entity.Property(e => e.UserNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.ForensicRootCause)
            .HasMaxLength(2048);

        entity.Property(e => e.ForensicConfidence)
            .HasDefaultValue(0.0);

        entity.Property(e => e.ReplaySafety)
            .HasMaxLength(32);

        entity.Property(e => e.ResolutionCause)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.CorrelationId)
            .HasMaxLength(256);

        entity.Property(e => e.SessionId)
            .HasMaxLength(256);

        entity.Property(e => e.TopicName)
            .HasMaxLength(512);

        // Unique constraint for deduplication: same message in same entity (per owner)
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.EntityName, e.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("IX_DlqMessages_Owner_Namespace_Entity_Sequence");

        // Index for querying by body hash (dedup across entities)
        entity.HasIndex(e => e.BodyHash)
            .HasDatabaseName("IX_DlqMessages_BodyHash");

        // Index for owner-scoped queries
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.Status })
            .HasDatabaseName("IX_DlqMessages_Owner_Namespace_Status");

        // Index for time-based queries
        entity.HasIndex(e => e.DetectedAtUtc)
            .HasDatabaseName("IX_DlqMessages_DetectedAt");

        // Index for failure category reporting
        entity.HasIndex(e => e.FailureCategory)
            .HasDatabaseName("IX_DlqMessages_FailureCategory");
    }

    private static void ConfigureReplayHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReplayHistory>();

        entity.ToTable("ReplayHistories");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.ReplayedBy)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.ReplayStrategy)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.ReplayedToEntity)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(e => e.OutcomeStatus)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.NewDeadLetterReason)
            .HasMaxLength(1024);

        entity.Property(e => e.ErrorDetails)
            .HasMaxLength(4096);

        // Relationships
        entity.HasOne(e => e.DlqMessage)
            .WithMany(m => m.ReplayHistories)
            .HasForeignKey(e => e.DlqMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Rule)
            .WithMany(r => r.ReplayHistories)
            .HasForeignKey(e => e.RuleId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasIndex(e => e.DlqMessageId)
            .HasDatabaseName("IX_ReplayHistories_DlqMessageId");

        entity.HasIndex(e => e.ReplayedAt)
            .HasDatabaseName("IX_ReplayHistories_ReplayedAt");
    }

    private static void ConfigureAutoReplayRule(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutoReplayRule>();

        entity.ToTable("AutoReplayRules");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Name)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.Description)
            .HasMaxLength(1024);

        entity.Property(e => e.ConditionsJson)
            .HasMaxLength(8192)
            .IsRequired();

        entity.Property(e => e.ActionsJson)
            .HasMaxLength(8192)
            .IsRequired();

        entity.Property(e => e.DisabledReason)
            .HasMaxLength(32);

        entity.Property(e => e.DisabledReasonDetail)
            .HasMaxLength(256);

        // Soft reference — no FK, matching NamespaceSignature.NamespaceId/AuditLog.NamespaceId.
        // NULL means fleet-wide (Global), unchanged from today's behavior.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId });
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLog>();

        entity.ToTable("AuditLogs");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.UserIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.Action)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.Outcome)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.NamespaceName)
            .HasMaxLength(256);

        entity.Property(e => e.EntityName)
            .HasMaxLength(512);

        entity.Property(e => e.CloudProvider)
            .HasMaxLength(32);

        entity.Property(e => e.Environment)
            .HasMaxLength(32);

        entity.Property(e => e.ResourceName)
            .HasMaxLength(512);

        entity.Property(e => e.DetailsJson)
            .HasMaxLength(8192);

        entity.Property(e => e.ErrorDetails)
            .HasMaxLength(4096);

        entity.Property(e => e.ClientIp)
            .HasMaxLength(64);

        entity.Property(e => e.UserAgent)
            .HasMaxLength(512);

        entity.Property(e => e.CorrelationId)
            .HasMaxLength(256);

        entity.Property(e => e.HttpMethod)
            .HasMaxLength(16);

        entity.Property(e => e.HttpPath)
            .HasMaxLength(1024);

        // Indexes optimised for the most common query patterns
        entity.HasIndex(e => e.Timestamp)
            .HasDatabaseName("IX_AuditLogs_Timestamp");

        entity.HasIndex(e => new { e.OwnerId, e.Timestamp })
            .HasDatabaseName("IX_AuditLogs_Owner_Timestamp");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.Timestamp })
            .HasDatabaseName("IX_AuditLogs_Owner_Namespace_Timestamp");

        entity.HasIndex(e => e.Action)
            .HasDatabaseName("IX_AuditLogs_Action");
    }

    private static void ConfigureBulkOperationJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BulkOperationJob>();

        entity.ToTable("BulkOperationJobs");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.OperationType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.NamespaceDisplayName)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.EntityNameFilter)
            .HasMaxLength(512);

        entity.Property(e => e.StatusFilter)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.CategoryFilter)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.FailureSampleJson)
            .HasMaxLength(8192);

        entity.Property(e => e.ErrorSummary)
            .HasMaxLength(2048);

        entity.Property(e => e.CorrelationId)
            .HasMaxLength(256);

        entity.Property(e => e.RequestedByIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.RequestedByActorKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.RequestedByScopes)
            .HasMaxLength(1024);

        // Owner-scoped job history, most recent first — the list endpoint's primary access path.
        entity.HasIndex(e => new { e.OwnerId, e.CreatedAt })
            .HasDatabaseName("IX_BulkOperationJobs_Owner_CreatedAt");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.CreatedAt })
            .HasDatabaseName("IX_BulkOperationJobs_Owner_Namespace_CreatedAt");

        // Worker startup scan for jobs left Pending/Running across a restart.
        entity.HasIndex(e => e.Status)
            .HasDatabaseName("IX_BulkOperationJobs_Status");
    }

    private static void ConfigureMessageFeatureRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MessageFeatureRecord>();

        entity.ToTable("MessageFeatureRecords");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.Provider)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.EntityName)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(e => e.DeadletterReason)
            .HasMaxLength(1024)
            .IsRequired();

        entity.Property(e => e.ExceptionType)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(e => e.ContentType)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.PayloadShape)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.ErrorTextNormalised)
            .HasMaxLength(8192)
            .IsRequired();

        entity.Property(e => e.SchemaFingerprint)
            .HasMaxLength(32)
            .IsRequired();

        // Cascade so feature rows can never outlive their parent message — the same
        // mechanism ReplayHistories relies on for its own cleanup (ConfigureReplayHistory).
        entity.HasOne(e => e.DlqMessage)
            .WithOne(m => m.FeatureRecord)
            .HasForeignKey<MessageFeatureRecord>(e => e.DlqMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.DlqMessageId)
            .IsUnique()
            .HasDatabaseName("IX_MessageFeatureRecords_DlqMessageId");

        // Query shapes clustering/baselining will use.
        entity.HasIndex(e => new { e.NamespaceId, e.CapturedAt })
            .HasDatabaseName("IX_MessageFeatureRecords_Namespace_CapturedAt");

        entity.HasIndex(e => new { e.NamespaceId, e.EntityName })
            .HasDatabaseName("IX_MessageFeatureRecords_Namespace_EntityName");
    }

    private static void ConfigureNamespaceSignature(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NamespaceSignature>();

        entity.ToTable("NamespaceSignatures");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.DominantDeadletterReason)
            .HasMaxLength(1024)
            .IsRequired();

        entity.Property(e => e.TopTermsJson)
            .HasMaxLength(2048)
            .IsRequired();

        // One row per distinct signature per namespace per owner — the upsert key. Includes
        // OwnerId (unlike the task's literal (NamespaceId, SignatureHash) spec) because
        // Namespace.SharedWithOwnerIds means two owners can legitimately observe the same
        // hash in the same namespace and must get independent, isolated rows.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash })
            .IsUnique()
            .HasDatabaseName("IX_NamespaceSignatures_Owner_Namespace_SignatureHash");

        // Owner-scoped queries.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId })
            .HasDatabaseName("IX_NamespaceSignatures_Owner_Namespace");
    }

    private static void ConfigureFailureKnowledge(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FailureKnowledgeEntity>();

        entity.ToTable("FailureKnowledge");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.RootCause)
            .HasMaxLength(4096);

        entity.Property(e => e.ResolutionNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.OperationalNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.RunbookLink)
            .HasMaxLength(1024);

        entity.Property(e => e.Owner)
            .HasMaxLength(256);

        entity.Property(e => e.ReplayGuidance)
            .HasMaxLength(32);

        entity.Property(e => e.KnowledgeVersion)
            .HasDefaultValue(1);

        entity.Property(e => e.Tags)
            .HasMaxLength(2048);

        entity.Property(e => e.UpdatedBy)
            .HasMaxLength(256);

        // One row per signature per namespace per owner
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash })
            .IsUnique()
            .HasDatabaseName("IX_FailureKnowledge_Owner_Namespace_SignatureHash");

        // Owner-scoped queries
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId })
            .HasDatabaseName("IX_FailureKnowledge_Owner_Namespace");

        // Find overdue knowledge reviews
        entity.HasIndex(e => new { e.ReviewDueAt })
            .HasDatabaseName("IX_FailureKnowledge_ReviewDueAt");
    }

    private static void ConfigureFailureKnowledgeHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FailureKnowledgeHistoryEntity>();

        entity.ToTable("FailureKnowledgeHistory");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.RootCause)
            .HasMaxLength(4096);

        entity.Property(e => e.ResolutionNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.OperationalNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.RunbookLink)
            .HasMaxLength(1024);

        entity.Property(e => e.Owner)
            .HasMaxLength(256);

        entity.Property(e => e.ReplayGuidance)
            .HasMaxLength(32);

        entity.Property(e => e.Tags)
            .HasMaxLength(2048);

        entity.Property(e => e.UpdatedBy)
            .HasMaxLength(256);

        // Version history lookups, ordered by version, per signature.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash, e.KnowledgeVersion })
            .HasDatabaseName("IX_FailureKnowledgeHistory_Owner_Namespace_SignatureHash_Version");
    }

    private static void ConfigureSignatureLifecycleState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SignatureLifecycleState>();

        entity.ToTable("SignatureLifecycleStates");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.Notes)
            .HasMaxLength(4096);

        // One current-state row per signature per namespace per owner — the upsert key.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash })
            .IsUnique()
            .HasDatabaseName("IX_SignatureLifecycleStates_Owner_Namespace_SignatureHash");
    }

    private static void ConfigureSignatureLifecycleHistoryEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SignatureLifecycleHistoryEntry>();

        entity.ToTable("SignatureLifecycleHistory");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.Notes)
            .HasMaxLength(4096);

        // Ordered transition history lookups per signature.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash, e.Timestamp })
            .HasDatabaseName("IX_SignatureLifecycleHistory_Owner_Namespace_SignatureHash_Timestamp");
    }

    private static void ConfigureSignatureReplayJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SignatureReplayJob>();

        entity.ToTable("SignatureReplayJobs");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.NamespaceDisplayName)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.StatusFilter)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.MessageIdsJson)
            .IsRequired();

        entity.Property(e => e.FailureSampleJson)
            .HasMaxLength(8192);

        entity.Property(e => e.ErrorSummary)
            .HasMaxLength(2048);

        entity.Property(e => e.CorrelationId)
            .HasMaxLength(256);

        entity.Property(e => e.RequestedByIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.RequestedByActorKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.RequestedByScopes)
            .HasMaxLength(1024);

        // Owner+signature-scoped job history, most recent first — the history list endpoint's
        // primary access path.
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.SignatureHash, e.CreatedAt })
            .HasDatabaseName("IX_SignatureReplayJobs_Owner_Namespace_SignatureHash_CreatedAt");

        // Worker startup scan for jobs left Pending/Running across a restart.
        entity.HasIndex(e => e.Status)
            .HasDatabaseName("IX_SignatureReplayJobs_Status");
    }

    private static void ConfigureRecoveryOperation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecoveryOperation>();

        entity.ToTable("RecoveryOperations");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.Kind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Trigger)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.ActorIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.ActorKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.ActorScopes)
            .HasMaxLength(1024);

        entity.Property(e => e.Reason)
            .HasMaxLength(2048);

        entity.Property(e => e.IntentHeader)
            .HasMaxLength(128);

        entity.Property(e => e.NamespaceNameSnapshot)
            .HasMaxLength(256);

        entity.Property(e => e.ProviderSnapshot)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.EnvironmentSnapshot)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.ScopeDescription)
            .HasMaxLength(1024)
            .IsRequired();

        entity.Property(e => e.CorrelationId)
            .HasMaxLength(256);

        entity.Property(e => e.ServiceVersion)
            .HasMaxLength(32)
            .IsRequired();

        // Owner-scoped operation history, most recent first — the ledger list endpoint's
        // primary access path (a later phase).
        entity.HasIndex(e => new { e.OwnerId, e.OpenedAt })
            .HasDatabaseName("IX_RecoveryOperations_Owner_OpenedAt");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.OpenedAt })
            .HasDatabaseName("IX_RecoveryOperations_Owner_Namespace_OpenedAt");
    }

    private static void ConfigureRecoveryLedgerEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecoveryLedgerEntry>();

        entity.ToTable("RecoveryLedgerEntries");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.NamespaceNameSnapshot)
            .HasMaxLength(256);

        entity.Property(e => e.ProviderSnapshot)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.EnvironmentSnapshot)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.EntityNameSnapshot)
            .HasMaxLength(512);

        entity.Property(e => e.EntityTypeSnapshot)
            .HasMaxLength(32);

        entity.Property(e => e.TopicNameSnapshot)
            .HasMaxLength(512);

        entity.Property(e => e.SourceMessageIdSnapshot)
            .HasMaxLength(256);

        entity.Property(e => e.BodyHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.FailureCategorySnapshot)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.DeadLetterReasonSnapshot)
            .HasMaxLength(1024);

        entity.Property(e => e.SignatureHashSnapshot)
            .HasMaxLength(64);

        entity.Property(e => e.RecoveryMarker)
            .HasMaxLength(64);

        entity.Property(e => e.TargetEntity)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.Disposition)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.VerificationResult)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.VerificationConfidence)
            .HasConversion<string>()
            .HasMaxLength(16);

        // Deliberately NO relationship to DlqMessage: DlqMessageId is a soft reference so that
        // cascade delete of a DlqMessage can never destroy ledger evidence (see RecoveryLedgerEntry
        // doc comment and LedgerNoForeignKeyTests).

        // Ageing report and open-entry lookups.
        entity.HasIndex(e => new { e.OwnerId, e.State, e.BegunAt })
            .HasDatabaseName("IX_RecoveryLedgerEntries_Owner_State_BegunAt");

        entity.HasIndex(e => e.OperationId)
            .HasDatabaseName("IX_RecoveryLedgerEntries_OperationId");

        // Body-hash recurrence lookup (heuristic verification fallback).
        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.EntityNameSnapshot, e.BodyHash })
            .HasDatabaseName("IX_RecoveryLedgerEntries_Owner_Namespace_Entity_BodyHash");

        // Exact-match recurrence lookup via the stamped marker; unique only where the marker was
        // actually applied.
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

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.EventType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.ActorIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.ActorKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.DetailJson)
            .HasMaxLength(8192);

        entity.Property(e => e.PrevHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.EntryHash)
            .HasMaxLength(64)
            .IsRequired();

        // The hash chain's ordering key: one contiguous, gap-free sequence per owner. Also the
        // backstop against concurrent appends racing past the in-process per-owner semaphore.
        entity.HasIndex(e => new { e.OwnerId, e.Seq })
            .IsUnique()
            .HasDatabaseName("IX_RecoveryEvents_Owner_Seq");

        entity.HasIndex(e => new { e.EntryId, e.Seq })
            .HasDatabaseName("IX_RecoveryEvents_EntryId_Seq");

        entity.HasIndex(e => new { e.OperationId, e.Seq })
            .HasDatabaseName("IX_RecoveryEvents_OperationId_Seq");

        // Predicate 0's hot-path read (roadmap §9.4.2): keeps IsEmergencyStopActiveAsync an
        // indexed lookup — it runs on every Eligibility Gate evaluation — rather than a linear
        // scan of an owner's full event history.
        entity.HasIndex(e => new { e.OwnerId, e.EventType, e.Seq })
            .HasDatabaseName("IX_RecoveryEvents_Owner_EventType_Seq");
    }

    private static void ConfigureAutonomyGrant(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutonomyGrant>();

        entity.ToTable("AutonomyGrants");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignatureHash)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.ActionKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.CurrentLevel)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Concurrency token: guards against two AutonomyEvaluationWorker instances (or two
        // overlapping sweep cycles) racing on the same row's UPDATE. Deliberately not
        // UpdatedAtUtc — see AutonomyGrant.ConcurrencyStamp's doc comment (roadmap §9.4.4
        // Correction). Stamped centrally in SaveChanges/SaveChangesAsync, never by a caller.
        entity.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        // Protects the first-creation race: .IsConcurrencyToken() governs UPDATE conflicts
        // only, so two workers independently deciding this triple has newly earned a grant and
        // both attempting an INSERT need a separate, additive guard (roadmap §9.4.4 Correction).
        entity.HasIndex(e => new { e.OwnerId, e.SignatureHash, e.ActionKind })
            .IsUnique()
            .HasDatabaseName("IX_AutonomyGrants_Owner_SignatureHash_ActionKind");
    }

    private static void ConfigureNamespace(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Core.Entities.Namespace>();

        entity.ToTable("Namespaces");
        entity.HasKey(e => e.Id);

        // Durable form lives in NamespaceSharedOwners (see ConfigureNamespaceSharedOwner) — the
        // repository hydrates this property after querying rather than EF mapping it directly,
        // since a plain string-list column would forfeit filterability for the same reason the
        // persistence design rejected it for AutoReplayRule.NamespaceId.
        entity.Ignore(e => e.SharedWithOwnerIds);

        entity.Property(e => e.Name)
            .HasMaxLength(Core.Entities.Namespace.MaxNameLength)
            .IsRequired();

        entity.Property(e => e.DisplayName)
            .HasMaxLength(Core.Entities.Namespace.MaxDisplayNameLength);

        entity.Property(e => e.Description)
            .HasMaxLength(Core.Entities.Namespace.MaxDescriptionLength);

        // Ciphertext moves byte-for-byte from the JSON store's ConnectionString field — same
        // ENC[v1]/legacy envelope, no re-encryption. Column renamed for clarity at rest; the
        // domain property name (ConnectionString) is unchanged.
        entity.Property(e => e.ConnectionString)
            .HasColumnName("ConnectionStringEncrypted");

        entity.Property(e => e.ConnectionStringHash)
            .HasMaxLength(64);

        entity.Property(e => e.AuthType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(e => e.Environment)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Provider)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.AwsRegion)
            .HasMaxLength(64);

        entity.Property(e => e.GcpProjectId)
            .HasMaxLength(128);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.HasIndex(e => new { e.OwnerId, e.Name })
            .IsUnique()
            .HasDatabaseName("IX_Namespaces_OwnerId_Name");

        entity.HasIndex(e => e.OwnerId)
            .HasDatabaseName("IX_Namespaces_OwnerId");

        entity.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_Namespaces_IsActive");
    }

    private static void ConfigureNamespaceSharedOwner(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NamespaceSharedOwner>();

        entity.ToTable("NamespaceSharedOwners");
        entity.HasKey(e => new { e.NamespaceId, e.OwnerId });

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        // The one deliberate real FK in the whole M1-M4 wave: sharing metadata has no
        // evidentiary value, so deleting a namespace should delete who it was shared with —
        // unlike every ledger-adjacent NamespaceId, which stays a soft reference.
        entity.HasOne<Core.Entities.Namespace>()
            .WithMany()
            .HasForeignKey(e => e.NamespaceId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.OwnerId)
            .HasDatabaseName("IX_NamespaceSharedOwners_OwnerId");
    }

    private static void ConfigureGovernanceGrant(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GovernanceGrant>();

        entity.ToTable("GovernanceGrants");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.GranteeIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.GranteeKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.PillarKind)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.GrantedByIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.RevokedByIdentity)
            .HasMaxLength(256);

        // No FK on NamespaceId — soft reference, same convention as every other ledger-adjacent
        // NamespaceId in this wave.

        entity.HasIndex(e => new { e.OwnerId, e.GranteeIdentity })
            .HasDatabaseName("IX_GovernanceGrants_OwnerId_GranteeIdentity");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId })
            .HasDatabaseName("IX_GovernanceGrants_OwnerId_NamespaceId");

        // Prevents two simultaneously-active grants for the same (grantee, namespace, pillar)
        // scope from silently disagreeing on Role. NOTE (SQL NULL semantics, not fixed here so as
        // not to silently deviate from the approved design): SQLite (like standard SQL) treats
        // NULL as distinct from NULL for uniqueness purposes, so this index does NOT actually
        // prevent duplicate fleet-wide/all-pillar (NamespaceId=null, PillarKind=null) grants for
        // the same grantee — worth knowing for whoever builds the enforcement layer this schema
        // supports (roadmap item 10), since a duplicate there would only ever be redundant, never
        // a security gap (grants are additive-permissive, never restrictive).
        entity.HasIndex(e => new { e.OwnerId, e.GranteeIdentity, e.NamespaceId, e.PillarKind })
            .IsUnique()
            .HasFilter("[RevokedAt] IS NULL")
            .HasDatabaseName("IX_GovernanceGrants_ActiveScope_Unique");
    }

    private static void ConfigurePlaybookEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlaybookEntry>();

        entity.ToTable("PlaybookEntries");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.PillarKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.ProposalKind)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.EvidenceRefJson)
            .IsRequired();

        entity.Property(e => e.ProposalJson)
            .IsRequired();

        entity.Property(e => e.ProposerIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.ProposerKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.SignatureHashSnapshot)
            .HasMaxLength(64);

        entity.Property(e => e.NamespaceNameSnapshot)
            .HasMaxLength(256);

        entity.Property(e => e.ProviderSnapshot)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.EnvironmentSnapshot)
            .HasConversion<string>()
            .HasMaxLength(16);

        entity.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Disposition)
            .HasConversion<string>()
            .HasMaxLength(16);

        // No FK anywhere on this entity — SignatureHashSnapshot, NamespaceId, and
        // RelatedRecoveryOperationId are all deliberate soft references, same convention as every
        // other ledger-adjacent table in this codebase.

        entity.HasIndex(e => new { e.OwnerId, e.State })
            .HasDatabaseName("IX_PlaybookEntries_OwnerId_State");

        entity.HasIndex(e => new { e.OwnerId, e.PillarKind })
            .HasDatabaseName("IX_PlaybookEntries_OwnerId_PillarKind");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId })
            .HasDatabaseName("IX_PlaybookEntries_OwnerId_NamespaceId");
    }

    private static void ConfigurePlaybookEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlaybookEvent>();

        entity.ToTable("PlaybookEvents");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.EventType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.ActorIdentity)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(e => e.ActorKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // No FK to PlaybookEntries (EntryId is a plain scalar column) and no FK of any kind to any
        // Recovery ledger table — this chain must never become, or depend on, a cascade target.

        entity.HasIndex(e => new { e.OwnerId, e.Seq })
            .IsUnique()
            .HasDatabaseName("IX_PlaybookEvents_OwnerId_Seq");

        entity.HasIndex(e => new { e.EntryId, e.Seq })
            .HasDatabaseName("IX_PlaybookEvents_EntryId_Seq");
    }

    private static void ConfigureExternalSignalEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ExternalSignalEvent>();

        entity.ToTable("ExternalSignalEvents");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(e => e.SignalType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(e => e.Source)
            .HasMaxLength(256)
            .IsRequired();

        // No FK to Namespaces — NamespaceId is a soft reference, same convention as every other
        // NamespaceId column in this schema (null means fleet-wide). No hash chain either — this
        // is raw external input, not a system claim; see the entity's own remarks.

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.OccurredAt })
            .HasDatabaseName("IX_ExternalSignalEvents_OwnerId_NamespaceId_OccurredAt");
    }
}
