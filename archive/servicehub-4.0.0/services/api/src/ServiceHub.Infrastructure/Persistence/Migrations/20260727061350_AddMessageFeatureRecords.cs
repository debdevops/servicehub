using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageFeatureRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageFeatureRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DlqMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeliveryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BodySizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TimeToDeadletterSeconds = table.Column<double>(type: "REAL", nullable: false),
                    SecondsSinceEnqueued = table.Column<double>(type: "REAL", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DeadletterReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayloadShape = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorTextNormalised = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    SchemaFingerprint = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FeatureVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageFeatureRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageFeatureRecords_DlqMessages_DlqMessageId",
                        column: x => x.DlqMessageId,
                        principalTable: "DlqMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeatureRecords_DlqMessageId",
                table: "MessageFeatureRecords",
                column: "DlqMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeatureRecords_Namespace_CapturedAt",
                table: "MessageFeatureRecords",
                columns: new[] { "NamespaceId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeatureRecords_Namespace_EntityName",
                table: "MessageFeatureRecords",
                columns: new[] { "NamespaceId", "EntityName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageFeatureRecords");
        }
    }
}
