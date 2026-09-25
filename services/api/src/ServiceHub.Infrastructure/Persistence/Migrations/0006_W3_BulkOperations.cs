using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W3_BulkOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkOperationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PreviewedAt = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<string>(type: "TEXT", nullable: true),
                    PerSecond = table.Column<double>(type: "REAL", nullable: false),
                    StopAfterConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    EndedReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkOperationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulkOperationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DlqMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DeadLetterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Remedy = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RecoveryEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkOperationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulkOperationItems_BulkOperationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "BulkOperationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulkOperationItems_Job_Position",
                table: "BulkOperationItems",
                columns: new[] { "JobId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkOperationJobs_Owner_Status",
                table: "BulkOperationJobs",
                columns: new[] { "OwnerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkOperationItems");

            migrationBuilder.DropTable(
                name: "BulkOperationJobs");
        }
    }
}
