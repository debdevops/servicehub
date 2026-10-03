using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W3_NamespaceSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureHash",
                table: "DlqMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NamespaceSignatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DominantDeadletterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ExampleError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    TopTermsJson = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceSignatures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_Owner_Namespace_Signature",
                table: "DlqMessages",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_Hash",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NamespaceSignatures");

            migrationBuilder.DropIndex(
                name: "IX_DlqMessages_Owner_Namespace_Signature",
                table: "DlqMessages");

            migrationBuilder.DropColumn(
                name: "SignatureHash",
                table: "DlqMessages");
        }
    }
}
