using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    HeroImageUrl = table.Column<string>(type: "text", nullable: false),
                    ShortDescription = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Neighborhood = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    SuggestedDurationMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recommendations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TravelPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsSubscription = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelPackages_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TravelerName = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    ConfirmationCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Slug",
                table: "Destinations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId",
                table: "Recommendations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId",
                table: "Reservations",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelPackages_DestinationId",
                table: "TravelPackages",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelPackages_Slug",
                table: "TravelPackages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationId",
                table: "Trips",
                column: "DestinationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recommendations");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "TravelPackages");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "Destinations");
        }
    }
}
