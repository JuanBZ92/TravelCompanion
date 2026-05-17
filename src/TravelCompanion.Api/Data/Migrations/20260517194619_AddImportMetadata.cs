using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Trips",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Reservations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "Reservations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Reservations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurationNotes",
                table: "Recommendations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Recommendations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "Recommendations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Recommendations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AppUserId_ExternalId",
                table: "Trips",
                columns: new[] { "AppUserId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_ExternalId",
                table: "Reservations",
                columns: new[] { "TripId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId_ExternalId",
                table: "Recommendations",
                columns: new[] { "DestinationId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_AppUserId_ExternalId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_ExternalId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId_ExternalId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CurationNotes",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Recommendations");
        }
    }
}
