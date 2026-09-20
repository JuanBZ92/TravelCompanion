using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccountAnalyticsAndDraftLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DraftPurgedAtUtc",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "StorePurchaseIntents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "StorePurchaseIntents",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBusinessEvent",
                table: "ProductAnalyticsEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "ProductAnalyticsEvents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "BehaviorAnalyticsConsent",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInternal",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DraftPurgedAtUtc",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                table: "StorePurchaseIntents");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "StorePurchaseIntents");

            migrationBuilder.DropColumn(
                name: "IsBusinessEvent",
                table: "ProductAnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "ProductAnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "BehaviorAnalyticsConsent",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "IsInternal",
                table: "AppUsers");
        }
    }
}
