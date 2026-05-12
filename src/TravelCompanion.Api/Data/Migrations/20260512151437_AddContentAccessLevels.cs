using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentAccessLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessLevel",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Paid");

            migrationBuilder.AddColumn<string>(
                name: "AccessLevel",
                table: "Recommendations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Free");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessLevel",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AccessLevel",
                table: "Recommendations");
        }
    }
}
