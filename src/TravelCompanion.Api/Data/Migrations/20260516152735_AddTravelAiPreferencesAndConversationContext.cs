using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelAiPreferencesAndConversationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TravelChatConversations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LastDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastResponseMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LastRecommendationIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelChatConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelChatConversations_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TravelerPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Interests = table.Column<List<string>>(type: "text[]", nullable: false),
                    FoodPreferences = table.Column<List<string>>(type: "text[]", nullable: false),
                    DietaryRestrictions = table.Column<List<string>>(type: "text[]", nullable: false),
                    Dislikes = table.Column<List<string>>(type: "text[]", nullable: false),
                    BudgetLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TravelPace = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AvoidTouristTraps = table.Column<bool>(type: "boolean", nullable: false),
                    MaxWalkingMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelerPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TravelerPreferences_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TravelChatConversations_UserId_UpdatedAt",
                table: "TravelChatConversations",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TravelChatConversations");

            migrationBuilder.DropTable(
                name: "TravelerPreferences");
        }
    }
}
