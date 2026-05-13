using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppUserId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AppUserId",
                table: "Trips",
                column: "AppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_AppUsers_AppUserId",
                table: "Trips",
                column: "AppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_AppUsers_AppUserId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_AppUserId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "Trips");
        }
    }
}
