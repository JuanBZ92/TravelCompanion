using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductExperimentAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductExperimentAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Experiment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductExperimentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductExperimentAssignments_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductExperimentAssignments_AppUserId_Experiment",
                table: "ProductExperimentAssignments",
                columns: new[] { "AppUserId", "Experiment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductExperimentAssignments");
        }
    }
}
