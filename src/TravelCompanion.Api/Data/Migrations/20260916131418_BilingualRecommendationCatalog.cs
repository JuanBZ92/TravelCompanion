using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BilingualRecommendationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraDescription",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraDescriptionEn",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPriceKnown",
                table: "Recommendations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalPrice",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPlaceId",
                table: "Recommendations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefinedType",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefinedTypeEn",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservationInstructions",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservationInstructionsEn",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservationSource",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationConfidence",
                table: "Recommendations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationNotes",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_DestinationId_ProviderPlaceId",
                table: "Recommendations",
                columns: new[] { "DestinationId", "ProviderPlaceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recommendations_DestinationId_ProviderPlaceId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ExtraDescription",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ExtraDescriptionEn",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "IsPriceKnown",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "OriginalPrice",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ProviderPlaceId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "RefinedType",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "RefinedTypeEn",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ReservationInstructions",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ReservationInstructionsEn",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "ReservationSource",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "VerificationConfidence",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "VerificationNotes",
                table: "Recommendations");
        }
    }
}
