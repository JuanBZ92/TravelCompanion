using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelPreferenceProfilesAndItinerarySave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TravelerPreferences_AppUsers_UserId",
                table: "TravelerPreferences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TravelerPreferences",
                table: "TravelerPreferences");

            migrationBuilder.RenameTable(
                name: "TravelerPreferences",
                newName: "TravelPreferenceProfiles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TravelPreferenceProfiles",
                table: "TravelPreferenceProfiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TravelPreferenceProfiles_AppUsers_UserId",
                table: "TravelPreferenceProfiles",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TravelPreferenceProfiles_AppUsers_UserId",
                table: "TravelPreferenceProfiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TravelPreferenceProfiles",
                table: "TravelPreferenceProfiles");

            migrationBuilder.RenameTable(
                name: "TravelPreferenceProfiles",
                newName: "TravelerPreferences");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TravelerPreferences",
                table: "TravelerPreferences",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TravelerPreferences_AppUsers_UserId",
                table: "TravelerPreferences",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
