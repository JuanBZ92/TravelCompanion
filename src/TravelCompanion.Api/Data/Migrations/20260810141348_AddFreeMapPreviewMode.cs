using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeMapPreviewMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CitySlug",
                table: "Recommendations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                table: "AppUserSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Trip");

            migrationBuilder.CreateTable(
                name: "FreeMapCities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CitySlug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CenterLatitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    CenterLongitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    FreeRadiusKm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CoverageRadiusKm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ContactUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeMapCities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreeMapCities_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId_CitySlug",
                table: "Recommendations",
                columns: new[] { "DestinationId", "CitySlug" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeMapCities_DestinationId_CitySlug",
                table: "FreeMapCities",
                columns: new[] { "DestinationId", "CitySlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreeMapCities_IsEnabled_SortOrder",
                table: "FreeMapCities",
                columns: new[] { "IsEnabled", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FreeMapCities");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId_CitySlug",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "CitySlug",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                table: "AppUserSessions");
        }
    }
}
