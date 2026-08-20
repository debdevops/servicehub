using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyStopIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEvents_Owner_EventType_Seq",
                table: "RecoveryEvents",
                columns: new[] { "OwnerId", "EventType", "Seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecoveryEvents_Owner_EventType_Seq",
                table: "RecoveryEvents");
        }
    }
}
