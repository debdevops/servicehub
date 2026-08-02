using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailureKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailureKnowledge",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RootCause = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    OperationalNotes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    RunbookLink = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReplayGuidance = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    KnowledgeVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    ReviewDueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureKnowledge", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailureKnowledge_Owner_Namespace",
                table: "FailureKnowledge",
                columns: new[] { "OwnerId", "NamespaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FailureKnowledge_Owner_Namespace_SignatureHash",
                table: "FailureKnowledge",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureKnowledge_ReviewDueAt",
                table: "FailureKnowledge",
                column: "ReviewDueAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailureKnowledge");
        }
    }
}
