using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TravelPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserEntitlements_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserEntitlements_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserEntitlements_TravelPackages_TravelPackageId",
                        column: x => x.TravelPackageId,
                        principalTable: "TravelPackages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                table: "AppUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_DestinationId",
                table: "UserEntitlements",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_TravelPackageId",
                table: "UserEntitlements",
                column: "TravelPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEntitlements_UserId",
                table: "UserEntitlements",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserEntitlements");

            migrationBuilder.DropTable(
                name: "AppUsers");
        }
    }
}
