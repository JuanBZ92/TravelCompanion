using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTodayRecommendationPlanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecommendationId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecommendationInteractionSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Signal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DistanceMeters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationInteractionSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationInteractionSignals_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecommendationInteractionSignals_Recommendations_Recommenda~",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecommendationInteractionSignals_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_RecommendationId",
                table: "Reservations",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_RecommendationId",
                table: "Reservations",
                columns: new[] { "TripId", "RecommendationId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationInteractionSignals_RecommendationId_Signal",
                table: "RecommendationInteractionSignals",
                columns: new[] { "RecommendationId", "Signal" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationInteractionSignals_TripId",
                table: "RecommendationInteractionSignals",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationInteractionSignals_UserId_CreatedAtUtc",
                table: "RecommendationInteractionSignals",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationInteractionSignals_UserId_TripId_Recommendati~",
                table: "RecommendationInteractionSignals",
                columns: new[] { "UserId", "TripId", "RecommendationId", "Signal" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Recommendations_RecommendationId",
                table: "Reservations",
                column: "RecommendationId",
                principalTable: "Recommendations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Recommendations_RecommendationId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "RecommendationInteractionSignals");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_RecommendationId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_RecommendationId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RecommendationId",
                table: "Reservations");
        }
    }
}
