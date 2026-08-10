using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleItemPlanningKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanningKind",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ManualEvent");

            migrationBuilder.Sql(
                """
                UPDATE "Reservations"
                SET "PlanningKind" = CASE
                    WHEN "Type" IN ('Flight', 'Lodging') THEN 'ConfirmedReservation'
                    WHEN "Title" LIKE 'Reserva - %' THEN 'ConfirmedReservation'
                    WHEN "RecommendationId" IS NOT NULL THEN 'Recommendation'
                    ELSE 'ManualEvent'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanningKind",
                table: "Reservations");
        }
    }
}
