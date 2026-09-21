using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRequesterActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestedByActorKind",
                table: "SignatureReplayJobs",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestedByIdentity",
                table: "SignatureReplayJobs",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestedByScopes",
                table: "SignatureReplayJobs",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByActorKind",
                table: "BulkOperationJobs",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestedByIdentity",
                table: "BulkOperationJobs",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestedByScopes",
                table: "BulkOperationJobs",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedByActorKind",
                table: "SignatureReplayJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByIdentity",
                table: "SignatureReplayJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByScopes",
                table: "SignatureReplayJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByActorKind",
                table: "BulkOperationJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByIdentity",
                table: "BulkOperationJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByScopes",
                table: "BulkOperationJobs");
        }
    }
}
