using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W1_NamespacesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NamespaceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CloudProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ResourceName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    HttpPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

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
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastConnectionTestAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastConnectionTestSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
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

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Owner_Namespace_Timestamp",
                table: "AuditLogs",
                columns: new[] { "OwnerId", "NamespaceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Owner_Timestamp",
                table: "AuditLogs",
                columns: new[] { "OwnerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Namespaces");
        }
    }
}
