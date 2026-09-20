using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompletePlanningRoutesAndStoreRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessLevel",
                table: "ThematicRoutes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Premium");

            migrationBuilder.AddColumn<string>(
                name: "Pace",
                table: "ThematicRoutes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "balanced");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedDate",
                table: "ThematicRoutes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateSourceId",
                table: "ThematicRoutes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "ThematicRoutes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowEnd",
                table: "ThematicRoutes",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowStart",
                table: "ThematicRoutes",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPayload",
                table: "StoreNotificationReceipts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentContextJson",
                table: "ItineraryProposals",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedItemsJson",
                table: "ItineraryProposals",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowEnd",
                table: "ItineraryProposals",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(21, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "WindowEndsNextDay",
                table: "ItineraryProposals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowStart",
                table: "ItineraryProposals",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<Guid>(
                name: "RouteApplicationId",
                table: "ItineraryOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreRevocationMarkers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    EvidenceFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRevocationMarkers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThematicRouteApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThematicRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItineraryOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThematicRouteApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplications_ItineraryOperations_ItineraryOper~",
                        column: x => x.ItineraryOperationId,
                        principalTable: "ItineraryOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplications_ThematicRoutes_ThematicRouteId",
                        column: x => x.ThematicRouteId,
                        principalTable: "ThematicRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplications_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripSynchronizationWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripSynchronizationWorks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThematicRouteApplicationStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThematicRouteApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThematicRouteStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItineraryItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThematicRouteApplicationStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplicationStops_Reservations_ItineraryItemId",
                        column: x => x.ItineraryItemId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplicationStops_ThematicRouteApplications_The~",
                        column: x => x.ThematicRouteApplicationId,
                        principalTable: "ThematicRouteApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThematicRouteApplicationStops_ThematicRouteStops_ThematicRo~",
                        column: x => x.ThematicRouteStopId,
                        principalTable: "ThematicRouteStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreRevocationMarkers_Provider_Environment_EvidenceFingerp~",
                table: "StoreRevocationMarkers",
                columns: new[] { "Provider", "Environment", "EvidenceFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreRevocationMarkers_Provider_Environment_ProviderTransac~",
                table: "StoreRevocationMarkers",
                columns: new[] { "Provider", "Environment", "ProviderTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplications_ItineraryOperationId",
                table: "ThematicRouteApplications",
                column: "ItineraryOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplications_ThematicRouteId_CreatedAtUtc",
                table: "ThematicRouteApplications",
                columns: new[] { "ThematicRouteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplications_TripId",
                table: "ThematicRouteApplications",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplicationStops_ItineraryItemId",
                table: "ThematicRouteApplicationStops",
                column: "ItineraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplicationStops_ThematicRouteApplicationId_Th~",
                table: "ThematicRouteApplicationStops",
                columns: new[] { "ThematicRouteApplicationId", "ThematicRouteStopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteApplicationStops_ThematicRouteStopId",
                table: "ThematicRouteApplicationStops",
                column: "ThematicRouteStopId");

            migrationBuilder.CreateIndex(
                name: "IX_TripSynchronizationWorks_ProcessedAtUtc_NextAttemptAtUtc",
                table: "TripSynchronizationWorks",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TripSynchronizationWorks_TripId_Revision_Kind",
                table: "TripSynchronizationWorks",
                columns: new[] { "TripId", "Revision", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreRevocationMarkers");

            migrationBuilder.DropTable(
                name: "ThematicRouteApplicationStops");

            migrationBuilder.DropTable(
                name: "TripSynchronizationWorks");

            migrationBuilder.DropTable(
                name: "ThematicRouteApplications");

            migrationBuilder.DropColumn(
                name: "AccessLevel",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "Pace",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "PlannedDate",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "TemplateSourceId",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "WindowEnd",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "WindowStart",
                table: "ThematicRoutes");

            migrationBuilder.DropColumn(
                name: "ProtectedPayload",
                table: "StoreNotificationReceipts");

            migrationBuilder.DropColumn(
                name: "CurrentContextJson",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "ProtectedItemsJson",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "WindowEnd",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "WindowEndsNextDay",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "WindowStart",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "RouteApplicationId",
                table: "ItineraryOperations");
        }
    }
}

