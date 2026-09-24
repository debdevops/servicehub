using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W2_ReplayHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DlqMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<long>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoveryEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SourceEntity = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ReplayedAt = table.Column<string>(type: "TEXT", nullable: false),
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_DlqMessageId",
                table: "ReplayHistories",
                column: "DlqMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_Owner_Namespace_ReplayedAt",
                table: "ReplayHistories",
                columns: new[] { "OwnerId", "NamespaceId", "ReplayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayHistories_ReplayedAt",
                table: "ReplayHistories",
                column: "ReplayedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplayHistories");
        }
    }
}
