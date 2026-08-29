using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNamespacesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Namespaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConnectionStringEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    AuthType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConnectionTestAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConnectionTestSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
                    HasListenPermission = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasSendPermission = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasManagePermission = table.Column<bool>(type: "INTEGER", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AwsRegion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    GcpProjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConnectionStringHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Namespaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NamespaceSharedOwners",
                columns: table => new
                {
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceSharedOwners", x => new { x.NamespaceId, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_NamespaceSharedOwners_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_IsActive",
                table: "Namespaces",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_OwnerId",
                table: "Namespaces",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_OwnerId_Name",
                table: "Namespaces",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSharedOwners_OwnerId",
                table: "NamespaceSharedOwners",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NamespaceSharedOwners");

            migrationBuilder.DropTable(
                name: "Namespaces");
        }
    }
}
