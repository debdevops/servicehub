using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNamespaceSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NamespaceSignatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DominantDeadletterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TopTermsJson = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceSignatures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "NamespaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_SignatureHash",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NamespaceSignatures");
        }
    }
}
