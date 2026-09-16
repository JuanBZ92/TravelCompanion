using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TravelCompanion.Api.Data.Migrations;

[DbContext(typeof(TravelCompanionDbContext))]
[Migration("20260916220000_TravelerExactTimeReservations")]
public sealed class TravelerExactTimeReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE "Reservations" SET "PlanningKind" = 'ConfirmedReservation'
        WHERE "Owner" = 'Traveler' AND "TimePrecision" = 'Exact' AND "Type" = 'Event'
          AND "ItemSource" IN ('YukuRecommendation', 'GooglePlace')
          AND "PlanningKind" IN ('Recommendation', 'ManualEvent');
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Existing confirmations cannot safely be distinguished from converted personal plans.
    }
}
