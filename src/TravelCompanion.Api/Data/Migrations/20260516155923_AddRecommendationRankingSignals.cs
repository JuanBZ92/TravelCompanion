using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationRankingSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "Recommendations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceLevel",
                table: "Recommendations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "Recommendations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "Recommendations",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "PriceLevel",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Recommendations");
        }
    }
}
