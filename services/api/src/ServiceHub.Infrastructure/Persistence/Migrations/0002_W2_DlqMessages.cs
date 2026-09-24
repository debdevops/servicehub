using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W2_DlqMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DlqMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    BodyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CloudProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TopicName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EnqueuedTimeUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeadLetterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DeadLetterErrorDescription = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DeliveryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    MessageSize = table.Column<long>(type: "INTEGER", nullable: false),
                    BodyPreview = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ApplicationPropertiesJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolutionCause = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlqMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_BodyHash",
                table: "DlqMessages",
                column: "BodyHash");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_DetectedAt",
                table: "DlqMessages",
                column: "DetectedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_Owner_Namespace_Entity_Sequence",
                table: "DlqMessages",
                columns: new[] { "OwnerId", "NamespaceId", "EntityName", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DlqMessages_Owner_Namespace_Status",
                table: "DlqMessages",
                columns: new[] { "OwnerId", "NamespaceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DlqMessages");
        }
    }
}
