using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleTimeZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Trips",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Reservations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Destinations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.Sql("""
                UPDATE "Destinations"
                SET "TimeZoneId" = 'Asia/Tokyo'
                WHERE "Slug" = 'japon';

                UPDATE "Trips" AS trip
                SET "TimeZoneId" = destination."TimeZoneId"
                FROM "Destinations" AS destination
                WHERE trip."DestinationId" = destination."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Destinations");
        }
    }
}
