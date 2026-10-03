using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W3_AutoReplayRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoReplayRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MaxPerHour = table.Column<int>(type: "INTEGER", nullable: false),
                    WaitSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BackOff = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisabledReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DisabledDetail = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastAskedAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastAskedReason = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AskedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoReplayRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoReplayRules_Owner_Provider_Enabled",
                table: "AutoReplayRules",
                columns: new[] { "OwnerId", "Provider", "Enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoReplayRules");
        }
    }
}
