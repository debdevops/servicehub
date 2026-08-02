using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailureKnowledgeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "FailureKnowledge",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FailureKnowledgeHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KnowledgeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RootCause = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    OperationalNotes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    RunbookLink = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReplayGuidance = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReviewDueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureKnowledgeHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailureKnowledgeHistory_Owner_Namespace_SignatureHash_Version",
                table: "FailureKnowledgeHistory",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash", "KnowledgeVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailureKnowledgeHistory");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "FailureKnowledge");
        }
    }
}
