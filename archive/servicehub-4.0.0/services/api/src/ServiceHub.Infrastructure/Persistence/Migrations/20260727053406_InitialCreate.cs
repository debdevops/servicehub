using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NamespaceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CloudProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ResourceName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    HttpPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoReplayRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConditionsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    ActionsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatchCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxReplaysPerHour = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoReplayRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulkOperationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceDisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EntityNameFilter = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StatusFilter = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CategoryFilter = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FromFilter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ToFilter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalMatched = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureSampleJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationRequestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkOperationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DlqMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    BodyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CloudProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Azure"),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EnqueuedTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeadLetterTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeadLetterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DeadLetterErrorDescription = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DeliveryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    MessageSize = table.Column<long>(type: "INTEGER", nullable: false),
                    BodyPreview = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ApplicationPropertiesJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    FailureCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CategoryConfidence = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReplayedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplaySuccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserNotes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ForensicRootCause = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ForensicConfidence = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    ReplaySafety = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TopicName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlqMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReplayHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DlqMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReplayedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReplayedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ReplayStrategy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplayedToEntity = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    OutcomeStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NewDeadLetterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayHistories_AutoReplayRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutoReplayRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReplayHistories_DlqMessages_DlqMessageId",
                        column: x => x.DlqMessageId,
                        principalTable: "DlqMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Owner_Namespace_Timestamp",
                table: "AuditLogs",
                columns: new[] { "OwnerId", "NamespaceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Owner_Timestamp",
                table: "AuditLogs",
                columns: new[] { "OwnerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_BulkOperationJobs_Owner_CreatedAt",
                table: "BulkOperationJobs",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkOperationJobs_Owner_Namespace_CreatedAt",
                table: "BulkOperationJobs",
                columns: new[] { "OwnerId", "NamespaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkOperationJobs_Status",
                table: "BulkOperationJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_BodyHash",
                table: "DlqMessages",
                column: "BodyHash");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_DetectedAt",
                table: "DlqMessages",
                column: "DetectedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_FailureCategory",
                table: "DlqMessages",
                column: "FailureCategory");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_Owner_Namespace_Entity_Sequence",
                table: "DlqMessages",
                columns: new[] { "OwnerId", "NamespaceId", "EntityName", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_Owner_Namespace_Status",
                table: "DlqMessages",
                columns: new[] { "OwnerId", "NamespaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_DlqMessageId",
                table: "ReplayHistories",
                column: "DlqMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_ReplayedAt",
                table: "ReplayHistories",
                column: "ReplayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_RuleId",
                table: "ReplayHistories",
                column: "RuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BulkOperationJobs");

            migrationBuilder.DropTable(
                name: "ReplayHistories");

            migrationBuilder.DropTable(
                name: "AutoReplayRules");

            migrationBuilder.DropTable(
                name: "DlqMessages");
        }
    }
}
