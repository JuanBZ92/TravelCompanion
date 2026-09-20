using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceConversionProposalsAndRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Trips",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Flexibility",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Flexible");

            migrationBuilder.Sql("""
                UPDATE "Reservations"
                SET "Flexibility" = 'ConfirmedReservation'
                WHERE "PlanningKind" = 'ConfirmedReservation'
                   OR "Type" IN ('Flight', 'Lodging');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PinHash",
                table: "BuilderAccessGrants",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MaximumExpiresAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "BuilderAccessGrants",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseTransactionId",
                table: "BuilderAccessGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PurchasedAtUtc",
                table: "BuilderAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantDailyUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuilderAccessGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtcDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SuccessfulRequests = table.Column<int>(type: "integer", nullable: false),
                    ReservedRequests = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantDailyUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantDailyUsages_BuilderAccessGrants_BuilderAccessGrant~",
                        column: x => x.BuilderAccessGrantId,
                        principalTable: "BuilderAccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantUsageLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuilderAccessGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UtcDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantUsageLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantUsageLeases_BuilderAccessGrants_BuilderAccessGrant~",
                        column: x => x.BuilderAccessGrantId,
                        principalTable: "BuilderAccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailVerificationChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestIpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PreviousStateJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreviousRevision = table.Column<int>(type: "integer", nullable: false),
                    AppliedRevision = table.Column<int>(type: "integer", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UndoAvailableUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UndoneAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItineraryOperations_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Goal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BasedOnRevision = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItineraryProposals_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnonymousUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Platform = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    AccessState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaywallVariant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BehaviorConsent = table.Column<bool>(type: "boolean", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreNotificationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderNotificationId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreNotificationReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorePurchaseIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OpaqueAccountId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntryPoint = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaywallVariant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DraftSnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorePurchaseIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorePurchaseIntents_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorePurchaseIntents_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThematicRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Theme = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Origin = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    VisitMinutes = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTransferMinutes = table.Column<int>(type: "integer", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThematicRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThematicRoutes_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThematicRoutes_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThematicRoutes_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StorePurchaseTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ProviderOriginalTransactionId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ProductId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PurchasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "text", nullable: true),
                    AcknowledgedOrConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorePurchaseTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorePurchaseTransactions_StorePurchaseIntents_PurchaseInte~",
                        column: x => x.PurchaseIntentId,
                        principalTable: "StorePurchaseIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThematicRouteStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThematicRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTransferMinutes = table.Column<int>(type: "integer", nullable: true),
                    ItineraryItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThematicRouteStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThematicRouteStops_Recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThematicRouteStops_Reservations_ItineraryItemId",
                        column: x => x.ItineraryItemId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ThematicRouteStops_ThematicRoutes_ThematicRouteId",
                        column: x => x.ThematicRouteId,
                        principalTable: "ThematicRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuilderAccessGrants_PurchaseTransactionId",
                table: "BuilderAccessGrants",
                column: "PurchaseTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantDailyUsages_BuilderAccessGrantId_UtcDate",
                table: "AssistantDailyUsages",
                columns: new[] { "BuilderAccessGrantId", "UtcDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsageLeases_BuilderAccessGrantId_OperationKey",
                table: "AssistantUsageLeases",
                columns: new[] { "BuilderAccessGrantId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsageLeases_BuilderAccessGrantId_UtcDate_ExpiresAt~",
                table: "AssistantUsageLeases",
                columns: new[] { "BuilderAccessGrantId", "UtcDate", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationChallenges_Email_CreatedAtUtc",
                table: "EmailVerificationChallenges",
                columns: new[] { "Email", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryOperations_TripId_AppliedRevision",
                table: "ItineraryOperations",
                columns: new[] { "TripId", "AppliedRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryOperations_TripId_IdempotencyKey",
                table: "ItineraryOperations",
                columns: new[] { "TripId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryProposals_TripId_AppUserId_IdempotencyKey",
                table: "ItineraryProposals",
                columns: new[] { "TripId", "AppUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryProposals_TripId_ExpiresAtUtc",
                table: "ItineraryProposals",
                columns: new[] { "TripId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsEvents_AppUserId_OccurredAtUtc",
                table: "ProductAnalyticsEvents",
                columns: new[] { "AppUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsEvents_EventId",
                table: "ProductAnalyticsEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAnalyticsEvents_Name_OccurredAtUtc",
                table: "ProductAnalyticsEvents",
                columns: new[] { "Name", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreNotificationReceipts_Provider_Environment_ProviderNoti~",
                table: "StoreNotificationReceipts",
                columns: new[] { "Provider", "Environment", "ProviderNotificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorePurchaseIntents_AppUserId_TripId_State",
                table: "StorePurchaseIntents",
                columns: new[] { "AppUserId", "TripId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_StorePurchaseIntents_OpaqueAccountId",
                table: "StorePurchaseIntents",
                column: "OpaqueAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorePurchaseIntents_TripId",
                table: "StorePurchaseIntents",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_StorePurchaseTransactions_Provider_Environment_ProviderTran~",
                table: "StorePurchaseTransactions",
                columns: new[] { "Provider", "Environment", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorePurchaseTransactions_PurchaseIntentId",
                table: "StorePurchaseTransactions",
                column: "PurchaseIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRoutes_AppUserId_UpdatedAtUtc",
                table: "ThematicRoutes",
                columns: new[] { "AppUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRoutes_DestinationId_City_Theme_Status",
                table: "ThematicRoutes",
                columns: new[] { "DestinationId", "City", "Theme", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRoutes_TripId",
                table: "ThematicRoutes",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteStops_ItineraryItemId",
                table: "ThematicRouteStops",
                column: "ItineraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteStops_RecommendationId",
                table: "ThematicRouteStops",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_ThematicRouteStops_ThematicRouteId_SortOrder",
                table: "ThematicRouteStops",
                columns: new[] { "ThematicRouteId", "SortOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BuilderAccessGrants_StorePurchaseTransactions_PurchaseTrans~",
                table: "BuilderAccessGrants",
                column: "PurchaseTransactionId",
                principalTable: "StorePurchaseTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BuilderAccessGrants_StorePurchaseTransactions_PurchaseTrans~",
                table: "BuilderAccessGrants");

            migrationBuilder.DropTable(
                name: "AssistantDailyUsages");

            migrationBuilder.DropTable(
                name: "AssistantUsageLeases");

            migrationBuilder.DropTable(
                name: "EmailVerificationChallenges");

            migrationBuilder.DropTable(
                name: "ItineraryOperations");

            migrationBuilder.DropTable(
                name: "ItineraryProposals");

            migrationBuilder.DropTable(
                name: "ProductAnalyticsEvents");

            migrationBuilder.DropTable(
                name: "StoreNotificationReceipts");

            migrationBuilder.DropTable(
                name: "StorePurchaseTransactions");

            migrationBuilder.DropTable(
                name: "ThematicRouteStops");

            migrationBuilder.DropTable(
                name: "StorePurchaseIntents");

            migrationBuilder.DropTable(
                name: "ThematicRoutes");

            migrationBuilder.DropIndex(
                name: "IX_BuilderAccessGrants_PurchaseTransactionId",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Flexibility",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MaximumExpiresAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "PurchaseTransactionId",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "PurchasedAtUtc",
                table: "BuilderAccessGrants");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "PinHash",
                table: "BuilderAccessGrants",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
