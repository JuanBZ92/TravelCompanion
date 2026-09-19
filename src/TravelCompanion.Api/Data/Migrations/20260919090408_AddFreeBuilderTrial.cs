using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeBuilderTrial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuilderAccessGrants_AppUserId",
                table: "BuilderAccessGrants");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConvertedAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrial",
                table: "BuilderAccessGrants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TrialAssistantRequestsUsed",
                table: "BuilderAccessGrants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialDraftExpiresAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEditingExpiresAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEditingStartedAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_AppUserId_IsTrial",
                table: "BuilderAccessGrants",
                columns: new[] { "AppUserId", "IsTrial" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuilderAccessGrants_AppUserId_IsTrial",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "ConvertedAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "IsTrial",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "TrialAssistantRequestsUsed",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "TrialDraftExpiresAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "TrialEditingExpiresAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "TrialEditingStartedAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_AppUserId",
                table: "BuilderAccessGrants",
                column: "AppUserId");
        }
    }
}
