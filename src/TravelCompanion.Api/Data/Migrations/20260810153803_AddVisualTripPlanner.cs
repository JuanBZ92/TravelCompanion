using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualTripPlanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlanRevision",
                table: "Trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PublicationStatus",
                table: "Trips",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(
                """
                UPDATE "Trips"
                SET "PublishedAtUtc" = NOW(),
                    "UpdatedAtUtc" = NOW(),
                    "PublicationStatus" = 'Published';
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Reservations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Reservations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripDayBlockId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TripDayPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    HotelBase = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    BaseLatitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    BaseLongitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Introduction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripDayPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripDayPlans_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripPlanDrafts",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasePlanRevision = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PendingAccessPinHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPlanDrafts", x => x.TripId);
                    table.ForeignKey(
                        name: "FK_TripPlanDrafts_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripDayBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripDayPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CuratedDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AutofillEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripDayBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripDayBlocks_TripDayPlans_TripDayPlanId",
                        column: x => x.TripDayPlanId,
                        principalTable: "TripDayPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_PublicationStatus_StartsOn",
                table: "Trips",
                columns: new[] { "PublicationStatus", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripDayBlockId",
                table: "Reservations",
                column: "TripDayBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_TripDayBlocks_TripDayPlanId_PeriodKey",
                table: "TripDayBlocks",
                columns: new[] { "TripDayPlanId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripDayPlans_TripId_Date",
                table: "TripDayPlans",
                columns: new[] { "TripId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripDayPlans_TripId_DayNumber",
                table: "TripDayPlans",
                columns: new[] { "TripId", "DayNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_TripDayBlocks_TripDayBlockId",
                table: "Reservations",
                column: "TripDayBlockId",
                principalTable: "TripDayBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_TripDayBlocks_TripDayBlockId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "TripDayBlocks");

            migrationBuilder.DropTable(
                name: "TripPlanDrafts");

            migrationBuilder.DropTable(
                name: "TripDayPlans");

            migrationBuilder.DropIndex(
                name: "IX_Trips_PublicationStatus_StartsOn",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripDayBlockId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PlanRevision",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "TripDayBlockId",
                table: "Reservations");
        }
    }
}
