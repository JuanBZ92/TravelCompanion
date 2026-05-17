using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDeviceRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PushToken = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ScheduleRemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecommendationNotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisabledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeviceRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeviceRegistrations_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutboxItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DeepLink = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ScheduledForUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SkippedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutboxItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationOutboxItems_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationOutboxItems_Recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationOutboxItems_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeviceRegistrations_UserId_DisabledAtUtc",
                table: "NotificationDeviceRegistrations",
                columns: new[] { "UserId", "DisabledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeviceRegistrations_UserId_InstallationId",
                table: "NotificationDeviceRegistrations",
                columns: new[] { "UserId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxItems_DeduplicationKey",
                table: "NotificationOutboxItems",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxItems_RecommendationId",
                table: "NotificationOutboxItems",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxItems_ReservationId",
                table: "NotificationOutboxItems",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxItems_Status_ScheduledForUtc",
                table: "NotificationOutboxItems",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxItems_UserId_Status",
                table: "NotificationOutboxItems",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeviceRegistrations");

            migrationBuilder.DropTable(
                name: "NotificationOutboxItems");
        }
    }
}
