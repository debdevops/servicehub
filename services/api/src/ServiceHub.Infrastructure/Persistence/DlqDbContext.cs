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

        entity.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        entity.Property(e => e.UserNotes)
            .HasMaxLength(4096);

        entity.Property(e => e.ForensicRootCause)
            .HasMaxLength(2048);

        entity.Property(e => e.ForensicConfidence)
            .HasDefaultValue(0.0);

        entity.Property(e => e.ReplaySafety)
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

        // Owner-scoped job history, most recent first — the list endpoint's primary access path.
        entity.HasIndex(e => new { e.OwnerId, e.CreatedAt })
            .HasDatabaseName("IX_BulkOperationJobs_Owner_CreatedAt");

        entity.HasIndex(e => new { e.OwnerId, e.NamespaceId, e.CreatedAt })
            .HasDatabaseName("IX_BulkOperationJobs_Owner_Namespace_CreatedAt");

        // Worker startup scan for jobs left Pending/Running across a restart.
        entity.HasIndex(e => e.Status)
            .HasDatabaseName("IX_BulkOperationJobs_Status");
    }
}
