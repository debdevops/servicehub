using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDlqObserverAttestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DlqObserverAttestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ObserverReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DlqEntityName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastCanarySentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCanaryMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StalenessBoundMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlqObserverAttestations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlqObserverAttestations_Enabled",
                table: "DlqObserverAttestations",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_DlqObserverAttestations_Owner_Namespace",
                table: "DlqObserverAttestations",
                columns: new[] { "OwnerId", "NamespaceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DlqObserverAttestations");
        }
    }
}
