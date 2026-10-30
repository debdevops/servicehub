using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W5_TrustPreconditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreconditionsHash",
                table: "AutonomyGrants",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreconditionsHash",
                table: "AutonomyGrants");
        }
    }
}
