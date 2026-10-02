using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeQueryWorkloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s';");
            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_Unconfirmed",
                table: "StorePurchaseTransactions",
                column: "VerifiedAtUtc",
                filter: "NOT \"AcknowledgedOrConsumed\" AND \"ProtectedProviderToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseIntents_PendingAttempt",
                table: "StorePurchaseIntents",
                column: "LastAttemptAtUtc",
                filter: "\"State\" = 'Pending' AND \"ProtectedEvidence\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_BehaviorRetention",
                table: "ProductAnalyticsEvents",
                columns: new[] { "OccurredAtUtc", "Id" },
                filter: "NOT \"IsBusinessEvent\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s';");
            migrationBuilder.DropIndex(
                name: "IX_PurchaseTransactions_Unconfirmed",
                table: "StorePurchaseTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseIntents_PendingAttempt",
                table: "StorePurchaseIntents");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_BehaviorRetention",
                table: "ProductAnalyticsEvents");
        }
    }
}
