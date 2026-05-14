using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelCompanion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Airline",
                table: "Reservations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationAirport",
                table: "Reservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationName",
                table: "Reservations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndsAt",
                table: "Reservations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndsOn",
                table: "Reservations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlightNumber",
                table: "Reservations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginAirport",
                table: "Reservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginName",
                table: "Reservations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Reservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Event");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_Type_Date_StartsAt",
                table: "Reservations",
                columns: new[] { "TripId", "Type", "Date", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_Type_Date_StartsAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Airline",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DestinationAirport",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DestinationName",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "EndsAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "EndsOn",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "FlightNumber",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OriginAirport",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OriginName",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Reservations");
        }
    }
}
