using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureLifecycleAndReplayDurability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureLifecycleHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureLifecycleHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignatureLifecycleStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TransitionedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureLifecycleStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignatureReplayJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceDisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StatusFilter = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FromFilter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ToFilter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MessageIdsJson = table.Column<string>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_SignatureReplayJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureLifecycleHistory_Owner_Namespace_SignatureHash_Timestamp",
                table: "SignatureLifecycleHistory",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureLifecycleStates_Owner_Namespace_SignatureHash",
                table: "SignatureLifecycleStates",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureReplayJobs_Owner_Namespace_SignatureHash_CreatedAt",
                table: "SignatureReplayJobs",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureReplayJobs_Status",
                table: "SignatureReplayJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureLifecycleHistory");

            migrationBuilder.DropTable(
                name: "SignatureLifecycleStates");

            migrationBuilder.DropTable(
                name: "SignatureReplayJobs");
        }
    }
}
