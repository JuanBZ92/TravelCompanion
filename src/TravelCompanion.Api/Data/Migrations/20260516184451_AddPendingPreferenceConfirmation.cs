using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingPreferenceConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingPreferenceOriginalMessage",
                table: "TravelChatConversations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingPreferencePatchJson",
                table: "TravelChatConversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingPreferenceRequestedAt",
                table: "TravelChatConversations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingPreferenceOriginalMessage",
                table: "TravelChatConversations");

            migrationBuilder.DropColumn(
                name: "PendingPreferencePatchJson",
                table: "TravelChatConversations");

            migrationBuilder.DropColumn(
                name: "PendingPreferenceRequestedAt",
                table: "TravelChatConversations");
        }
    }
}
