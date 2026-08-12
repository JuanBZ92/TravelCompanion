using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfServiceItineraryBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExperienceMode",
                table: "Trips",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CuratedPremium");

            migrationBuilder.AddColumn<string>(
                name: "BaseAddress",
                table: "TripDayPlans",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BaseProviderPlaceId",
                table: "TripDayPlans",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemSource",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "Reservations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Yuku");

            migrationBuilder.AddColumn<string>(
                name: "ProviderPlaceId",
                table: "Reservations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimePrecision",
                table: "Reservations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Exact");

            migrationBuilder.Sql("""
                UPDATE "Reservations"
                SET "Owner" = 'Traveler',
                    "ItemSource" = CASE WHEN "RecommendationId" IS NULL THEN 'Manual' ELSE 'YukuRecommendation' END
                WHERE "SourceName" = 'Travel Assistant';
                """);

            migrationBuilder.CreateTable(
                name: "BuilderAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    PinHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OrderReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuilderAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuilderAccessGrants_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuilderAccessGrants_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuilderAccessGrants_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_Owner_Date",
                table: "Reservations",
                columns: new[] { "TripId", "Owner", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_ProviderPlaceId",
                table: "Reservations",
                columns: new[] { "TripId", "ProviderPlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_AppUserId",
                table: "BuilderAccessGrants",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_DestinationId",
                table: "BuilderAccessGrants",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_Status_ExpiresAtUtc",
                table: "BuilderAccessGrants",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_TripId",
                table: "BuilderAccessGrants",
                column: "TripId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuilderAccessGrants");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_Owner_Date",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_ProviderPlaceId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ExperienceMode",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "BaseAddress",
                table: "TripDayPlans");

            migrationBuilder.DropColumn(
                name: "BaseProviderPlaceId",
                table: "TripDayPlans");

            migrationBuilder.DropColumn(
                name: "ItemSource",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ProviderPlaceId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "TimePrecision",
                table: "Reservations");
        }
    }
}
