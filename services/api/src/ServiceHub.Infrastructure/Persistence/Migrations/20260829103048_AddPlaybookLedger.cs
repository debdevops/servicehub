using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybookLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybookEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PillarKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ProposalKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvidenceRefJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProposalJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProposedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProposerIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProposerKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SignatureHashSnapshot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NamespaceNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProviderSnapshot = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    EnvironmentSnapshot = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    RelatedRecoveryOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    LastEventSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Seq = table.Column<long>(type: "INTEGER", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: true),
                    PrevHash = table.Column<string>(type: "TEXT", nullable: false),
                    EntryHash = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookEntries_OwnerId_NamespaceId",
                table: "PlaybookEntries",
                columns: new[] { "OwnerId", "NamespaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookEntries_OwnerId_PillarKind",
                table: "PlaybookEntries",
                columns: new[] { "OwnerId", "PillarKind" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookEntries_OwnerId_State",
                table: "PlaybookEntries",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookEvents_EntryId_Seq",
                table: "PlaybookEvents",
                columns: new[] { "EntryId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookEvents_OwnerId_Seq",
                table: "PlaybookEvents",
                columns: new[] { "OwnerId", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybookEntries");

            migrationBuilder.DropTable(
                name: "PlaybookEvents");
        }
    }
}
