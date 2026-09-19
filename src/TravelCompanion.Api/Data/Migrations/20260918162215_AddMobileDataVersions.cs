using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileDataVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MobileDataVersions",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileDataVersions", x => x.Scope);
                });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION bump_mobile_data_version(scope_value text)
                RETURNS void AS $$
                BEGIN
                    INSERT INTO "MobileDataVersions" ("Scope", "Version", "UpdatedAtUtc")
                    VALUES (scope_value, 2, NOW())
                    ON CONFLICT ("Scope") DO UPDATE
                    SET "Version" = "MobileDataVersions"."Version" + 1,
                        "UpdatedAtUtc" = NOW();
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION bump_recommendation_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid := COALESCE(NEW."DestinationId", OLD."DestinationId");
                BEGIN
                    PERFORM bump_mobile_data_version('catalog:' || replace(destination_id::text, '-', ''));
                    PERFORM bump_mobile_data_version('free-catalog:' || replace(destination_id::text, '-', ''));
                    PERFORM bump_mobile_data_version('free-catalog:global');
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER recommendations_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "Recommendations"
                FOR EACH ROW EXECUTE FUNCTION bump_recommendation_versions();

                CREATE OR REPLACE FUNCTION bump_package_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid := COALESCE(NEW."DestinationId", OLD."DestinationId");
                BEGIN
                    PERFORM bump_mobile_data_version('catalog:' || replace(destination_id::text, '-', ''));
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER packages_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "TravelPackages"
                FOR EACH ROW EXECUTE FUNCTION bump_package_versions();

                CREATE OR REPLACE FUNCTION bump_package_membership_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid;
                BEGIN
                    SELECT COALESCE(r."DestinationId", p."DestinationId") INTO destination_id
                    FROM (SELECT COALESCE(NEW."RecommendationId", OLD."RecommendationId") AS id) x
                    LEFT JOIN "Recommendations" r ON r."Id" = x.id
                    LEFT JOIN "TravelPackages" p ON p."Id" = COALESCE(NEW."TravelPackageId", OLD."TravelPackageId");
                    IF destination_id IS NOT NULL THEN
                        PERFORM bump_mobile_data_version('catalog:' || replace(destination_id::text, '-', ''));
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER package_memberships_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "RecommendationTravelPackages"
                FOR EACH ROW EXECUTE FUNCTION bump_package_membership_versions();

                CREATE OR REPLACE FUNCTION bump_entitlement_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid := COALESCE(NEW."DestinationId", OLD."DestinationId");
                DECLARE package_id uuid := COALESCE(NEW."TravelPackageId", OLD."TravelPackageId");
                BEGIN
                    IF destination_id IS NULL AND package_id IS NOT NULL THEN
                        SELECT p."DestinationId" INTO destination_id
                        FROM "TravelPackages" p
                        WHERE p."Id" = package_id;
                    END IF;
                    IF destination_id IS NOT NULL THEN
                        PERFORM bump_mobile_data_version('catalog:' || replace(destination_id::text, '-', ''));
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER entitlements_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "UserEntitlements"
                FOR EACH ROW EXECUTE FUNCTION bump_entitlement_versions();

                CREATE OR REPLACE FUNCTION bump_destination_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid := COALESCE(NEW."Id", OLD."Id");
                BEGIN
                    PERFORM bump_mobile_data_version('catalog:' || replace(destination_id::text, '-', ''));
                    PERFORM bump_mobile_data_version('free-catalog:' || replace(destination_id::text, '-', ''));
                    PERFORM bump_mobile_data_version('free-catalog:global');
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER destinations_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "Destinations"
                FOR EACH ROW EXECUTE FUNCTION bump_destination_versions();

                CREATE OR REPLACE FUNCTION bump_free_map_versions()
                RETURNS trigger AS $$
                DECLARE destination_id uuid := COALESCE(NEW."DestinationId", OLD."DestinationId");
                BEGIN
                    PERFORM bump_mobile_data_version('free-catalog:' || replace(destination_id::text, '-', ''));
                    PERFORM bump_mobile_data_version('free-catalog:global');
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER free_map_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "FreeMapCities"
                FOR EACH ROW EXECUTE FUNCTION bump_free_map_versions();

                CREATE OR REPLACE FUNCTION bump_document_versions()
                RETURNS trigger AS $$
                DECLARE trip_id uuid := COALESCE(NEW."TripId", OLD."TripId");
                BEGIN
                    PERFORM bump_mobile_data_version('documents:' || replace(trip_id::text, '-', ''));
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER documents_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "TravelDocuments"
                FOR EACH ROW EXECUTE FUNCTION bump_document_versions();

                CREATE OR REPLACE FUNCTION bump_preference_versions()
                RETURNS trigger AS $$
                DECLARE user_id uuid := COALESCE(NEW."UserId", OLD."UserId");
                BEGIN
                    PERFORM bump_mobile_data_version('today:' || replace(user_id::text, '-', ''));
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER preferences_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "TravelPreferenceProfiles"
                FOR EACH ROW EXECUTE FUNCTION bump_preference_versions();

                CREATE OR REPLACE FUNCTION bump_signal_versions()
                RETURNS trigger AS $$
                DECLARE user_id uuid := COALESCE(NEW."UserId", OLD."UserId");
                DECLARE signal_value text := COALESCE(NEW."Signal", OLD."Signal");
                BEGIN
                    IF signal_value <> 'Suggested' THEN
                        PERFORM bump_mobile_data_version('today:' || replace(user_id::text, '-', ''));
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER signals_mobile_version
                AFTER INSERT OR UPDATE OR DELETE ON "RecommendationInteractionSignals"
                FOR EACH ROW EXECUTE FUNCTION bump_signal_versions();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS signals_mobile_version ON "RecommendationInteractionSignals";
                DROP TRIGGER IF EXISTS preferences_mobile_version ON "TravelPreferenceProfiles";
                DROP TRIGGER IF EXISTS documents_mobile_version ON "TravelDocuments";
                DROP TRIGGER IF EXISTS free_map_mobile_version ON "FreeMapCities";
                DROP TRIGGER IF EXISTS destinations_mobile_version ON "Destinations";
                DROP TRIGGER IF EXISTS entitlements_mobile_version ON "UserEntitlements";
                DROP TRIGGER IF EXISTS package_memberships_mobile_version ON "RecommendationTravelPackages";
                DROP TRIGGER IF EXISTS packages_mobile_version ON "TravelPackages";
                DROP TRIGGER IF EXISTS recommendations_mobile_version ON "Recommendations";
                DROP FUNCTION IF EXISTS bump_signal_versions();
                DROP FUNCTION IF EXISTS bump_preference_versions();
                DROP FUNCTION IF EXISTS bump_document_versions();
                DROP FUNCTION IF EXISTS bump_free_map_versions();
                DROP FUNCTION IF EXISTS bump_destination_versions();
                DROP FUNCTION IF EXISTS bump_entitlement_versions();
                DROP FUNCTION IF EXISTS bump_package_membership_versions();
                DROP FUNCTION IF EXISTS bump_package_versions();
                DROP FUNCTION IF EXISTS bump_recommendation_versions();
                DROP FUNCTION IF EXISTS bump_mobile_data_version(text);
                """);
            migrationBuilder.DropTable(
                name: "MobileDataVersions");
        }
    }
}
