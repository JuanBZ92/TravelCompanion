using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    MutationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalNotes_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalNotes_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalNotes_TripId",
                table: "JournalNotes",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalNotes_UserId_TripId_ActivityId",
                table: "JournalNotes",
                columns: new[] { "UserId", "TripId", "ActivityId" },
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "JournalNotes" ("Id", "UserId", "TripId", "ActivityId", "Title", "City", "Date", "Notes", "Revision", "MutationId", "UpdatedAt")
                SELECT r."Id", t."AppUserId", t."Id", r."Id", r."Title", r."City", r."Date", btrim(r."Notes"), 1,
                    '00000000-0000-0000-0000-000000000000'::uuid, now()
                FROM "Reservations" r JOIN "Trips" t ON t."Id" = r."TripId"
                LEFT JOIN "Recommendations" c ON c."Id" = r."RecommendationId"
                WHERE t."AppUserId" IS NOT NULL AND r."Owner" = 'Traveler'
                    AND btrim(r."Notes") <> ''
                    AND lower(btrim(r."Notes")) <> 'guardado desde travel assistant.'
                    AND btrim(r."Notes") IS DISTINCT FROM btrim(c."Description")
                    AND btrim(r."Notes") IS DISTINCT FROM btrim(c."DescriptionEn")
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalNotes");
        }
    }
}
