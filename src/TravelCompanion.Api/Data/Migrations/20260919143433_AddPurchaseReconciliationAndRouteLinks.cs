using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReconciliationAndRouteLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedProviderToken",
                table: "StorePurchaseTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Environment",
                table: "StorePurchaseIntents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedEvidence",
                table: "StorePurchaseIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceRouteId",
                table: "ItineraryProposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryProposals_SourceRouteId",
                table: "ItineraryProposals",
                column: "SourceRouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItineraryProposals_ThematicRoutes_SourceRouteId",
                table: "ItineraryProposals",
                column: "SourceRouteId",
                principalTable: "ThematicRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItineraryProposals_ThematicRoutes_SourceRouteId",
                table: "ItineraryProposals");

            migrationBuilder.DropIndex(
                name: "IX_ItineraryProposals_SourceRouteId",
                table: "ItineraryProposals");

            migrationBuilder.DropColumn(
                name: "ProtectedProviderToken",
                table: "StorePurchaseTransactions");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "StorePurchaseIntents");

            migrationBuilder.DropColumn(
                name: "ProtectedEvidence",
                table: "StorePurchaseIntents");

            migrationBuilder.DropColumn(
                name: "SourceRouteId",
                table: "ItineraryProposals");
        }
    }
}
