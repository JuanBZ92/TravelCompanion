using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripPinLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessPinHash",
                table: "Trips",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AccessPinUpdatedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripId",
                table: "AppUserSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserSessions_TripId_RevokedAt",
                table: "AppUserSessions",
                columns: new[] { "TripId", "RevokedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppUserSessions_Trips_TripId",
                table: "AppUserSessions",
                column: "TripId",
                principalTable: "Trips",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUserSessions_Trips_TripId",
                table: "AppUserSessions");

            migrationBuilder.DropIndex(
                name: "IX_AppUserSessions_TripId_RevokedAt",
                table: "AppUserSessions");

            migrationBuilder.DropColumn(
                name: "AccessPinHash",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "AccessPinUpdatedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TripId",
                table: "AppUserSessions");
        }
    }
}
