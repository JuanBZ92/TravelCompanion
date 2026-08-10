using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class TripWorkbookImportServiceTests
{
    [Fact]
    public async Task Template_contains_one_visible_sheet_and_hidden_dropdown_sources()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var bytes = await service.CreateTemplateAsync();

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(XLWorksheetVisibility.Visible, workbook.Worksheet("Crear viaje").Visibility);
        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("CatalogoRecommendations").Visibility);
        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("ListasDropdown").Visibility);
        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("Validaciones").Visibility);
        Assert.Equal("PIN", workbook.Worksheet("Crear viaje").Cell("A1").GetString());
        Assert.Equal("Location 3", workbook.Worksheet("Crear viaje").Cell("I9").GetString());
        Assert.Equal("Hora reserva", workbook.Worksheet("Crear viaje").Cell("K9").GetString());
    }

    [Fact]
    public async Task Generated_example_does_not_warn_for_autofill_notes()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var bytes = await service.CreateExampleWorkbookAsync();
        var preview = await service.PreviewAsync(new MemoryStream(bytes));

        Assert.DoesNotContain(
            preview.Rows.SelectMany(row => row.Warnings),
            warning => warning.Contains("autofill", StringComparison.OrdinalIgnoreCase));
        Assert.All(preview.Rows.Where(row => !row.IsReservation), row => Assert.Null(row.Time));
        Assert.All(preview.Rows.Where(row => row.IsReservation), row => Assert.NotNull(row.Time));
    }

    [Fact]
    public async Task Preview_rejects_pin_reserved_for_free_map()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "0000",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Tokyo", "Tarde", "autofill", "-", "-", "-", "No", "-", "-")
            ]);

        var preview = await service.PreviewAsync(new MemoryStream(workbookBytes));

        Assert.True(preview.HasErrors);
        Assert.Contains(preview.Errors, error => error.Contains("reservado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_creates_trip_user_pin_lodging_and_recommendation_reservations_idempotently()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        var ramen = CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]);
        var cafe = CreateRecommendation(destinationId, "Cafe One", "Tokyo", ["food", "cafe", "breakfast"]);
        dbContext.Recommendations.AddRange(ramen, cafe);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "2468",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Test Tokyo", "Tarde", "Tarde curada de ramen y paseo corto.", "Ramen One - Tokyo - Food", "-", "-", "No", "-", "-"),
                new WorkbookRow(2, "Tokyo", "Hotel Test Tokyo", "Mañana", "autofill", "-", "-", "-", "No", "-", "Dia sin curar."),
                new WorkbookRow(2, "Tokyo", "Hotel Test Tokyo", "Noche", "Cena confirmada.", "Ramen One - Tokyo - Food", "-", "-", "Si", "20:00", "A nombre del cliente."),
            ]);

        var preview = await service.PreviewAsync(new MemoryStream(workbookBytes));
        Assert.False(preview.HasErrors);
        Assert.Equal(3, preview.ValidRows);
        Assert.Equal(1, preview.AutofillRows);
        Assert.Equal(1, Assert.Single(preview.Rows, row => row.Day == 1).MatchedLocationCount);
        var autofillRow = Assert.Single(preview.Rows, row => row.Day == 2 && row.IsAutofill);
        Assert.True(autofillRow.IsAutofill);
        Assert.Empty(autofillRow.Warnings);

        var import = await service.ImportAsync(new MemoryStream(workbookBytes));
        Assert.True(import.Imported);
        Assert.True(import.CreatedTrip);
        Assert.Equal(3, import.CreatedReservations);

        var reimport = await service.ImportAsync(new MemoryStream(workbookBytes));
        Assert.True(reimport.Imported);
        Assert.True(reimport.UpdatedTrip);
        Assert.Equal(3, reimport.CreatedReservations);

        var trip = await dbContext.Trips
            .Include(existing => existing.AppUser)
            .Include(existing => existing.Reservations)
            .SingleAsync();
        Assert.Equal("Cliente Test", trip.TravelerName);
        Assert.Equal("Cliente Test", trip.AppUser?.DisplayName);
        Assert.Equal(new DateOnly(2026, 10, 1), trip.StartsOn);
        Assert.NotNull(trip.AccessPinHash);
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            new PasswordHasher<Trip>().VerifyHashedPassword(trip, trip.AccessPinHash!, "2468"));

        var importedReservations = trip.Reservations
            .Where(reservation => reservation.SourceName == TripWorkbookImportService.SourceName)
            .ToList();
        Assert.Equal(3, importedReservations.Count);
        var recommendationItem = Assert.Single(importedReservations, reservation =>
            reservation.Type == ReservationType.Event
            && reservation.PlanningKind == ScheduleItemKind.Recommendation);
        Assert.Equal(ramen.Id, recommendationItem.RecommendationId);
        Assert.Equal("Descripcion: Tarde curada de ramen y paseo corto.", recommendationItem.Notes);
        Assert.Single(importedReservations, reservation =>
            reservation.Type == ReservationType.Event
            && reservation.PlanningKind == ScheduleItemKind.ConfirmedReservation);
        Assert.Single(importedReservations, reservation =>
            reservation.Type == ReservationType.Lodging
            && reservation.PlanningKind == ScheduleItemKind.ConfirmedReservation);

        var todayService = new TodayRecommendationService(
            dbContext,
            NullLogger<TodayRecommendationService>.Instance);
        var today = await todayService.GetTodayAsync(
            trip.AppUser!,
            trip.Id,
            new DateOnly(2026, 10, 1),
            null,
            CancellationToken.None);
        var afternoon = Assert.Single(today!.Sections, section => section.PeriodKey == "afternoon");
        Assert.Equal("Tarde curada de ramen y paseo corto.", afternoon.Description);
        Assert.DoesNotContain(afternoon.Reservations, item => item.PlanningKind == ScheduleItemKind.Recommendation);
        Assert.Contains(afternoon.Reservations, item => item.Type == ReservationType.Lodging);
        var assignedLocation = Assert.Single(afternoon.Recommendations);
        Assert.Equal(ramen.Id, assignedLocation.Recommendation.Id);
        Assert.True(assignedLocation.IsAssigned);
        Assert.Equal("Seleccionada para este bloque", assignedLocation.RankReason);
    }

    [Fact]
    public async Task Preview_ignores_time_when_row_is_not_a_reservation()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "2468",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Test Tokyo", "Tarde", "Tarde curada.", "Ramen One - Tokyo - Food", "-", "-", "No", "16:00", "-"),
            ]);

        var preview = await service.PreviewAsync(new MemoryStream(workbookBytes));

        var row = Assert.Single(preview.Rows);
        Assert.False(preview.HasErrors);
        Assert.Null(row.Time);
        Assert.Contains(row.Warnings, warning => warning.Contains("La hora se ignoro", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_requires_time_for_a_confirmed_reservation()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "2468",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Test Tokyo", "Noche", "Cena confirmada.", "Ramen One - Tokyo - Food", "-", "-", "Si", "-", "-"),
            ]);

        var preview = await service.PreviewAsync(new MemoryStream(workbookBytes));

        Assert.True(preview.HasErrors);
        Assert.Contains(preview.Errors, error => error.Contains("Hora reserva es obligatoria", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_accepts_legacy_hora_header()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "2468",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Test Tokyo", "Noche", "Cena confirmada.", "Ramen One - Tokyo - Food", "-", "-", "Si", "20:00", "-"),
            ]);
        using var workbook = new XLWorkbook(new MemoryStream(workbookBytes));
        workbook.Worksheet("Crear viaje").Cell("K9").Value = "Hora";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var preview = await service.PreviewAsync(new MemoryStream(stream.ToArray()));

        Assert.False(preview.HasErrors);
        Assert.Equal("20:00", Assert.Single(preview.Rows).Time);
    }

    [Fact]
    public async Task Today_allocates_next_ranked_recommendations_to_later_free_days_without_repeating()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "today-allocation@travelcompanion.local",
            DisplayName = "Today Allocation",
            PasswordHash = string.Empty
        };
        var tripId = Guid.NewGuid();
        var startsOn = new DateOnly(2026, 10, 1);
        var trip = new Trip
        {
            Id = tripId,
            AppUserId = user.Id,
            DestinationId = destinationId,
            TravelerName = user.DisplayName,
            StartsOn = startsOn,
            EndsOn = startsOn.AddDays(1),
            TimeZoneId = "Asia/Tokyo",
            Reservations =
            [
                CreateBlockingEvent(tripId, startsOn, new TimeOnly(12, 0), "Lunch day 1"),
                CreateBlockingEvent(tripId, startsOn, new TimeOnly(15, 0), "Afternoon day 1"),
                CreateBlockingEvent(tripId, startsOn, new TimeOnly(20, 0), "Night day 1"),
                CreateBlockingEvent(tripId, startsOn.AddDays(1), new TimeOnly(12, 0), "Lunch day 2"),
                CreateBlockingEvent(tripId, startsOn.AddDays(1), new TimeOnly(15, 0), "Afternoon day 2"),
                CreateBlockingEvent(tripId, startsOn.AddDays(1), new TimeOnly(20, 0), "Night day 2")
            ]
        };
        dbContext.AppUsers.Add(user);
        dbContext.Trips.Add(trip);
        dbContext.Recommendations.AddRange(
            CreateRecommendation(destinationId, "Cafe A", "Tokyo", ["food", "cafe", "breakfast"]),
            CreateRecommendation(destinationId, "Cafe B", "Tokyo", ["food", "cafe", "breakfast"]),
            CreateRecommendation(destinationId, "Cafe C", "Tokyo", ["food", "cafe", "breakfast"]),
            CreateRecommendation(destinationId, "Cafe D", "Tokyo", ["food", "cafe", "breakfast"]));
        await dbContext.SaveChangesAsync();
        var service = new TodayRecommendationService(
            dbContext,
            NullLogger<TodayRecommendationService>.Instance);

        var dayTwo = await service.GetTodayAsync(
            user,
            tripId,
            startsOn.AddDays(1),
            null,
            CancellationToken.None);
        var dayOne = await service.GetTodayAsync(
            user,
            tripId,
            startsOn,
            null,
            CancellationToken.None);
        var dayTwoAgain = await service.GetTodayAsync(
            user,
            tripId,
            startsOn.AddDays(1),
            null,
            CancellationToken.None);

        var dayOneMorning = Assert.Single(dayOne!.Sections, section => section.PeriodKey == "morning");
        var dayTwoMorning = Assert.Single(dayTwo!.Sections, section => section.PeriodKey == "morning");
        var dayTwoMorningAgain = Assert.Single(dayTwoAgain!.Sections, section => section.PeriodKey == "morning");
        Assert.Equal(["Cafe A", "Cafe B"], dayOneMorning.Recommendations.Select(item => item.Recommendation.Title));
        Assert.Equal(["Cafe C", "Cafe D"], dayTwoMorning.Recommendations.Select(item => item.Recommendation.Title));
        Assert.Empty(dayOneMorning.Recommendations.Select(item => item.Recommendation.Id)
            .Intersect(dayTwoMorning.Recommendations.Select(item => item.Recommendation.Id)));
        Assert.Equal(
            dayTwoMorning.Recommendations.Select(item => item.Recommendation.Id),
            dayTwoMorningAgain.Recommendations.Select(item => item.Recommendation.Id));
        Assert.Equal(4, await dbContext.RecommendationInteractionSignals.CountAsync(signal =>
            signal.TripId == tripId
            && signal.Signal == RecommendationSignal.Suggested
            && signal.Source.StartsWith("today_auto:")));
    }

    [Fact]
    public async Task Preview_warns_when_autofill_contains_scheduled_content()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        dbContext.Recommendations.Add(CreateRecommendation(destinationId, "Ramen One", "Tokyo", ["food", "ramen"]));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var workbookBytes = await CreateImportWorkbookAsync(
            service,
            pin: "2468",
            travelerName: "Cliente Test",
            rows:
            [
                new WorkbookRow(1, "Tokyo", "Hotel Test Tokyo", "Tarde", "autofill", "Ramen One - Tokyo - Food", "-", "-", "No", "16:00", "Nota operativa."),
            ]);

        var preview = await service.PreviewAsync(new MemoryStream(workbookBytes));

        var row = Assert.Single(preview.Rows);
        Assert.True(row.IsAutofill);
        Assert.Contains(row.Warnings, warning => warning.Contains("locations, reserva u horario", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseSeeder_removes_legacy_dummy_content_but_keeps_yuku_recommendations_free()
    {
        await using var dbContext = CreateDbContext();
        var destinationId = await SeedJapanDestinationAsync(dbContext);
        var legacyPackage = new TravelPackage
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Name = "Japon Essentials",
            Slug = "japon-essentials",
            Description = "Legacy",
            Price = 19,
            Currency = "USD"
        };
        var legacyRecommendation = CreateRecommendation(destinationId, "Seed Culture", "Tokyo", ["culture"]);
        legacyRecommendation.SourceName = null;
        var yukuRecommendation = CreateRecommendation(destinationId, "YUKU Ramen", "Tokyo", ["food", "ramen"]);
        yukuRecommendation.SourceName = YukuJapanRecommendationImportService.SourceName;
        yukuRecommendation.AccessLevel = ContentAccessLevel.Paid;
        yukuRecommendation.Packages.Add(legacyPackage);
        legacyRecommendation.Packages.Add(legacyPackage);

        var demoUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "demo@travelcompanion.local",
            DisplayName = "Demo Traveler",
            PasswordHash = string.Empty
        };
        dbContext.AppUsers.Add(demoUser);
        dbContext.TravelPackages.Add(legacyPackage);
        dbContext.Recommendations.AddRange(legacyRecommendation, yukuRecommendation);
        dbContext.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            AppUserId = demoUser.Id,
            DestinationId = destinationId,
            TravelerName = "Demo Traveler",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 2),
            Reservations =
            [
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    Type = ReservationType.Event,
                    Date = new DateOnly(2026, 10, 1),
                    StartsAt = new TimeOnly(10, 0),
                    Title = "Demo event",
                    City = "Tokyo",
                    LocationName = "Demo",
                    Address = "Tokyo",
                    ConfirmationCode = "DEMO",
                    Notes = "Demo"
                }
            ]
        });
        await dbContext.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(dbContext, new PasswordHasher<AppUser>());

        var remainingRecommendation = await dbContext.Recommendations
            .Include(recommendation => recommendation.Packages)
            .SingleAsync();
        Assert.Equal(yukuRecommendation.Id, remainingRecommendation.Id);
        Assert.Equal(ContentAccessLevel.Free, remainingRecommendation.AccessLevel);
        Assert.Empty(remainingRecommendation.Packages);
        Assert.False(await dbContext.AppUsers.AnyAsync(user => user.Email == "demo@travelcompanion.local"));
        Assert.False(await dbContext.Trips.AnyAsync());
        Assert.False(await dbContext.TravelPackages.AnyAsync(package => package.Slug == "japon-essentials"));
    }

    private static TripWorkbookImportService CreateService(TravelCompanionDbContext dbContext)
    {
        return new TripWorkbookImportService(
            dbContext,
            new PasswordHasher<Trip>(),
            NullLogger<TripWorkbookImportService>.Instance);
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TravelCompanionDbContext(options);
    }

    private static async Task<Guid> SeedJapanDestinationAsync(TravelCompanionDbContext dbContext)
    {
        var destinationId = Guid.NewGuid();
        dbContext.Destinations.Add(new Destination
        {
            Id = destinationId,
            Name = "Japon",
            Slug = "japon",
            Country = "Japan",
            TimeZoneId = "Asia/Tokyo",
            HeroImageUrl = string.Empty,
            ShortDescription = "Japan"
        });
        await dbContext.SaveChangesAsync();
        return destinationId;
    }

    private static Recommendation CreateRecommendation(
        Guid destinationId,
        string title,
        string city,
        IReadOnlyList<string> tags) =>
        new()
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            ExternalId = $"yuku-japan-{city.ToLowerInvariant()}-{title.ToLowerInvariant().Replace(' ', '-')}",
            Title = title,
            Category = "Food",
            Neighborhood = $"{city}, Japan",
            Description = $"{title} description",
            Tags = tags.ToList(),
            PriceLevel = "medium",
            Latitude = 35.67m,
            Longitude = 139.76m,
            SuggestedDurationMinutes = 60,
            Rating = 4.2,
            AccessLevel = ContentAccessLevel.Free,
            SourceName = YukuJapanRecommendationImportService.SourceName
        };

    private static Reservation CreateBlockingEvent(
        Guid tripId,
        DateOnly date,
        TimeOnly startsAt,
        string title) =>
        new()
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            Type = ReservationType.Event,
            PlanningKind = ScheduleItemKind.ManualEvent,
            Date = date,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(1),
            TimeZoneId = "Asia/Tokyo",
            Title = title,
            City = "Tokyo",
            LocationName = title,
            Address = "Tokyo",
            ConfirmationCode = string.Empty,
            Notes = string.Empty
        };

    private static async Task<byte[]> CreateImportWorkbookAsync(
        TripWorkbookImportService service,
        string pin,
        string travelerName,
        IReadOnlyList<WorkbookRow> rows)
    {
        var templateBytes = await service.CreateTemplateAsync();
        using var workbook = new XLWorkbook(new MemoryStream(templateBytes));
        var sheet = workbook.Worksheet("Crear viaje");
        var startsOn = new DateOnly(2026, 10, 1);

        sheet.Cell("B1").Value = pin;
        sheet.Cell("B2").Value = travelerName;
        sheet.Cell("B3").Value = "japon";
        sheet.Cell("B4").Value = startsOn.ToDateTime(TimeOnly.MinValue);
        sheet.Cell("B5").Value = startsOn.AddDays(2).ToDateTime(TimeOnly.MinValue);
        sheet.Cell("B6").Value = "Asia/Tokyo";

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = 10 + index;
            sheet.Cell(excelRow, 1).Value = row.Day;
            sheet.Cell(excelRow, 2).Value = startsOn.AddDays(row.Day - 1).ToDateTime(TimeOnly.MinValue);
            sheet.Cell(excelRow, 3).Value = row.City;
            sheet.Cell(excelRow, 4).Value = row.HotelBase;
            sheet.Cell(excelRow, 5).Value = row.Moment;
            sheet.Cell(excelRow, 6).Value = row.Description;
            sheet.Cell(excelRow, 7).Value = row.Location1;
            sheet.Cell(excelRow, 8).Value = row.Location2;
            sheet.Cell(excelRow, 9).Value = row.Location3;
            sheet.Cell(excelRow, 10).Value = row.Reservation;
            sheet.Cell(excelRow, 11).Value = row.Time;
            sheet.Cell(excelRow, 12).Value = row.Notes;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record WorkbookRow(
        int Day,
        string City,
        string HotelBase,
        string Moment,
        string Description,
        string Location1,
        string Location2,
        string Location3,
        string Reservation,
        string Time,
        string Notes);
}
