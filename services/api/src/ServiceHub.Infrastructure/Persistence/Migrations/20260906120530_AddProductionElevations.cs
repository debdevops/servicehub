using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionElevations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionElevations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RequestedByIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ApprovedByIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ExpiredEventRecorded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionElevations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionElevations_Owner_ExpiredEventRecorded_ExpiresAt",
                table: "ProductionElevations",
                columns: new[] { "OwnerId", "ExpiredEventRecorded", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionElevations_Owner_Namespace_Approved_Revoked",
                table: "ProductionElevations",
                columns: new[] { "OwnerId", "NamespaceId", "ApprovedAt", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionElevations");
        }
    }
}
