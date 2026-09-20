using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernanceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GranteeIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    GranteeKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PillarKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantedByIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceGrants_ActiveScope_Unique",
                table: "GovernanceGrants",
                columns: new[] { "OwnerId", "GranteeIdentity", "NamespaceId", "PillarKind" },
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceGrants_OwnerId_GranteeIdentity",
                table: "GovernanceGrants",
                columns: new[] { "OwnerId", "GranteeIdentity" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceGrants_OwnerId_NamespaceId",
                table: "GovernanceGrants",
                columns: new[] { "OwnerId", "NamespaceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernanceGrants");
        }
    }
}
