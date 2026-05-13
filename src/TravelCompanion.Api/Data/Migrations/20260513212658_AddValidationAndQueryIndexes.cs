using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationAndQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_DestinationId",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_TravelPackageId",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_UserId",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_Trips_AppUserId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DestinationId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_TravelPackages_DestinationId",
                table: "TravelPackages");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId",
                table: "Recommendations");

            migrationBuilder.DropIndex(
                name: "IX_AppUserSessions_UserId",
                table: "AppUserSessions");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_DestinationId_ExpiresAt",
                table: "UserEntitlements",
                columns: new[] { "DestinationId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_TravelPackageId_ExpiresAt",
                table: "UserEntitlements",
                columns: new[] { "TravelPackageId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_UserId_ExpiresAt",
                table: "UserEntitlements",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AppUserId_StartsOn",
                table: "Trips",
                columns: new[] { "AppUserId", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationId_StartsOn",
                table: "Trips",
                columns: new[] { "DestinationId", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelPackages_DestinationId_Price",
                table: "TravelPackages",
                columns: new[] { "DestinationId", "Price" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_Date_StartsAt",
                table: "Reservations",
                columns: new[] { "TripId", "Date", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId_Category_Title",
                table: "Recommendations",
                columns: new[] { "DestinationId", "Category", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId_Title",
                table: "Recommendations",
                columns: new[] { "DestinationId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserSessions_UserId_RevokedAt",
                table: "AppUserSessions",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_DestinationId_ExpiresAt",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_TravelPackageId_ExpiresAt",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_UserEntitlements_UserId_ExpiresAt",
                table: "UserEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_Trips_AppUserId_StartsOn",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DestinationId_StartsOn",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_TravelPackages_DestinationId_Price",
                table: "TravelPackages");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_Date_StartsAt",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId_Category_Title",
                table: "Recommendations");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId_Title",
                table: "Recommendations");

            migrationBuilder.DropIndex(
                name: "IX_AppUserSessions_UserId_RevokedAt",
                table: "AppUserSessions");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_DestinationId",
                table: "UserEntitlements",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_TravelPackageId",
                table: "UserEntitlements",
                column: "TravelPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_UserId",
                table: "UserEntitlements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AppUserId",
                table: "Trips",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationId",
                table: "Trips",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelPackages_DestinationId",
                table: "TravelPackages",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId",
                table: "Reservations",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId",
                table: "Recommendations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUserSessions_UserId",
                table: "AppUserSessions",
                column: "UserId");
        }
    }
}
