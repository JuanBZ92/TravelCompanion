using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelAssistantFeedbackAndConversationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastIntent",
                table: "TravelChatConversations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLocale",
                table: "TravelChatConversations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPromptVersion",
                table: "TravelChatConversations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateJson",
                table: "TravelChatConversations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TravelAssistantFeedbackItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Signal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Intent = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ResponseMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelAssistantFeedbackItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelAssistantFeedbackItems_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TravelAssistantFeedbackItems_Recommendations_Recommendation~",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TravelAssistantFeedbackItems_TravelChatConversations_Conver~",
                        column: x => x.ConversationId,
                        principalTable: "TravelChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TravelAssistantFeedbackItems_ConversationId_CreatedAtUtc",
                table: "TravelAssistantFeedbackItems",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelAssistantFeedbackItems_RecommendationId_Signal",
                table: "TravelAssistantFeedbackItems",
                columns: new[] { "RecommendationId", "Signal" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelAssistantFeedbackItems_UserId_CreatedAtUtc",
                table: "TravelAssistantFeedbackItems",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TravelAssistantFeedbackItems");

            migrationBuilder.DropColumn(
                name: "LastIntent",
                table: "TravelChatConversations");

            migrationBuilder.DropColumn(
                name: "LastLocale",
                table: "TravelChatConversations");

            migrationBuilder.DropColumn(
                name: "LastPromptVersion",
                table: "TravelChatConversations");

            migrationBuilder.DropColumn(
                name: "StateJson",
                table: "TravelChatConversations");
        }
    }
}
