using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryEvidenceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecoveryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Seq = table.Column<long>(type: "INTEGER", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    PrevHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntryHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DlqMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NamespaceNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProviderSnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    EnvironmentSnapshot = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    EntityNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EntityTypeSnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TopicNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceMessageIdSnapshot = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SourceSequenceNumberSnapshot = table.Column<long>(type: "INTEGER", nullable: true),
                    BodyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FailureCategorySnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DeadLetterReasonSnapshot = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SignatureHashSnapshot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TargetEntity = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BegunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecoveryMarker = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MarkerApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    VerificationResult = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    VerificationConfidence = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ObservationWindowEndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastEventSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ActorScopes = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IntentHeader = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NamespaceNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProviderSnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    EnvironmentSnapshot = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ScopeDescription = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SourceRuleId = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceJobId = table.Column<long>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ServiceVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TargetCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEvents_EntryId_Seq",
                table: "RecoveryEvents",
                columns: new[] { "EntryId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEvents_OperationId_Seq",
                table: "RecoveryEvents",
                columns: new[] { "OperationId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEvents_Owner_Seq",
                table: "RecoveryEvents",
                columns: new[] { "OwnerId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryLedgerEntries_OperationId",
                table: "RecoveryLedgerEntries",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryLedgerEntries_Owner_Namespace_Entity_BodyHash",
                table: "RecoveryLedgerEntries",
                columns: new[] { "OwnerId", "NamespaceId", "EntityNameSnapshot", "BodyHash" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryLedgerEntries_Owner_State_BegunAt",
                table: "RecoveryLedgerEntries",
                columns: new[] { "OwnerId", "State", "BegunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryLedgerEntries_RecoveryMarker",
                table: "RecoveryLedgerEntries",
                column: "RecoveryMarker",
                unique: true,
                filter: "[RecoveryMarker] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryOperations_Owner_Namespace_OpenedAt",
                table: "RecoveryOperations",
                columns: new[] { "OwnerId", "NamespaceId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryOperations_Owner_OpenedAt",
                table: "RecoveryOperations",
                columns: new[] { "OwnerId", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryEvents");

            migrationBuilder.DropTable(
                name: "RecoveryLedgerEntries");

            migrationBuilder.DropTable(
                name: "RecoveryOperations");
        }
    }
}
