using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for DLQ Intelligence data.
/// Uses SQLite for lightweight, file-based persistence.
/// </summary>
public sealed class DlqDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DlqDbContext"/> class.
    /// </summary>
    public DlqDbContext(DbContextOptions<DlqDbContext> options) : base(options)
    {
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
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecoveryLedgerAppendOnlyGuard.Enforce(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
    }
}
