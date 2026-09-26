using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentFreeAndConversionCohorts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductAnalyticsDailyAggregates_Date_Name_Source_Platform_A~",
                table: "ProductAnalyticsDailyAggregates");

            migrationBuilder.AddColumn<string>(
                name: "FreePolicyVariant",
                table: "ProductAnalyticsEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreePolicyVariant",
                table: "ProductAnalyticsDailyAggregates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FreePolicy",
                table: "BuilderAccessGrants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsDailyAggregates_Date_Name_Source_Platform_A~",
                table: "ProductAnalyticsDailyAggregates",
                columns: new[] { "Date", "Name", "Source", "Platform", "AppVersion", "PaywallVariant", "FreePolicyVariant" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductAnalyticsDailyAggregates_Date_Name_Source_Platform_A~",
                table: "ProductAnalyticsDailyAggregates");

            migrationBuilder.DropColumn(
                name: "FreePolicyVariant",
                table: "ProductAnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "FreePolicyVariant",
                table: "ProductAnalyticsDailyAggregates");

            migrationBuilder.DropColumn(
                name: "FreePolicy",
                table: "BuilderAccessGrants");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsDailyAggregates_Date_Name_Source_Platform_A~",
                table: "ProductAnalyticsDailyAggregates",
                columns: new[] { "Date", "Name", "Source", "Platform", "AppVersion", "PaywallVariant" },
                unique: true);
        }
    }
}
