using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsDailyAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductAnalyticsDailyAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Platform = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaywallVariant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EventCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAnalyticsDailyAggregates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsDailyAggregates_Date_Name_Source_Platform_A~",
                table: "ProductAnalyticsDailyAggregates",
                columns: new[] { "Date", "Name", "Source", "Platform", "AppVersion", "PaywallVariant" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductAnalyticsDailyAggregates");
        }
    }
}
