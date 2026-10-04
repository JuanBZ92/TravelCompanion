using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDayPlanReplayReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s';");
            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "AssistantUsageLeases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseJson",
                table: "AssistantUsageLeases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanningApplicationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MutationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningApplicationReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningApplicationReceipts_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningApplicationReceipts_AppUserId_TripId",
                table: "PlanningApplicationReceipts",
                columns: new[] { "AppUserId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningApplicationReceipts_TripId_MutationId",
                table: "PlanningApplicationReceipts",
                columns: new[] { "TripId", "MutationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s';");
            migrationBuilder.DropTable(
                name: "PlanningApplicationReceipts");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "AssistantUsageLeases");

            migrationBuilder.DropColumn(
                name: "ResponseJson",
                table: "AssistantUsageLeases");
        }
    }
}
