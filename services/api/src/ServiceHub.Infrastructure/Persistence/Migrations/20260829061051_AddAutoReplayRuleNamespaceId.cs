using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReplayRuleNamespaceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NamespaceId",
                table: "AutoReplayRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoReplayRules_OwnerId_NamespaceId",
                table: "AutoReplayRules",
                columns: new[] { "OwnerId", "NamespaceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutoReplayRules_OwnerId_NamespaceId",
                table: "AutoReplayRules");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "AutoReplayRules");
        }
    }
}
