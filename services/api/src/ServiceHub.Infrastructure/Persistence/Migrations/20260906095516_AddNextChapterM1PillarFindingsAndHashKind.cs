using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNextChapterM1PillarFindingsAndHashKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_SignatureHash",
                table: "NamespaceSignatures");

            // ADR-0009 Decision unit 2 (M1.4): every existing row defaults to Fingerprint, the
            // roadmap's own stated default for a row that can't yet be classified — recomputing
            // ClusterSignatureHasher.ComputeHash requires C# (SHA-256 over normalised terms), not
            // expressible in a migration's raw SQL, so the actual classification happens in
            // NamespaceSignatureHashKindBackfiller immediately after this migration applies (see
            // Program.cs). SignatureHash itself is never touched by either step — only this new
            // column is written.
            migrationBuilder.AddColumn<string>(
                name: "HashKind",
                table: "NamespaceSignatures",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Fingerprint");

            migrationBuilder.CreateTable(
                name: "Anomalies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anomalies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacklogForecasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CurrentBacklogCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GrowthRatePerHour = table.Column<double>(type: "REAL", nullable: false),
                    AlertThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectedHoursToBreach = table.Column<double>(type: "REAL", nullable: false),
                    ProjectedBreachAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacklogForecasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CorrelationFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Providers = table.Column<string>(type: "TEXT", nullable: false),
                    Members = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrelationFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriftFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalSignalCorrelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AnomalyType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AnomalySeverity = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SignalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignalType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SignalSource = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SignalOccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Gap = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSignalCorrelations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Narrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NamespaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccessNamespaceIds = table.Column<string>(type: "TEXT", nullable: false),
                    Headline = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContributingAnomalyIds = table.Column<string>(type: "TEXT", nullable: false),
                    ContributingDriftFindingIds = table.Column<string>(type: "TEXT", nullable: false),
                    ContributingCorrelationFindingIds = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedActions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Narrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_HashKind",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "HashKind" });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_SignatureHash_HashKind",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash", "HashKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_Owner_Namespace_DetectedAt",
                table: "Anomalies",
                columns: new[] { "OwnerId", "NamespaceId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BacklogForecasts_Owner_Namespace_DetectedAt",
                table: "BacklogForecasts",
                columns: new[] { "OwnerId", "NamespaceId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrelationFindings_Owner_DetectedAt",
                table: "CorrelationFindings",
                columns: new[] { "OwnerId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriftFindings_Owner_Namespace_DetectedAt",
                table: "DriftFindings",
                columns: new[] { "OwnerId", "NamespaceId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignalCorrelations_Owner_Namespace_DetectedAt",
                table: "ExternalSignalCorrelations",
                columns: new[] { "OwnerId", "NamespaceId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Narrations_GeneratedAt",
                table: "Narrations",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Narrations_NamespaceId",
                table: "Narrations",
                column: "NamespaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Anomalies");

            migrationBuilder.DropTable(
                name: "BacklogForecasts");

            migrationBuilder.DropTable(
                name: "CorrelationFindings");

            migrationBuilder.DropTable(
                name: "DriftFindings");

            migrationBuilder.DropTable(
                name: "ExternalSignalCorrelations");

            migrationBuilder.DropTable(
                name: "Narrations");

            migrationBuilder.DropIndex(
                name: "IX_NamespaceSignatures_Owner_HashKind",
                table: "NamespaceSignatures");

            migrationBuilder.DropIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_SignatureHash_HashKind",
                table: "NamespaceSignatures");

            migrationBuilder.DropColumn(
                name: "HashKind",
                table: "NamespaceSignatures");

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceSignatures_Owner_Namespace_SignatureHash",
                table: "NamespaceSignatures",
                columns: new[] { "OwnerId", "NamespaceId", "SignatureHash" },
                unique: true);
        }
    }
}
