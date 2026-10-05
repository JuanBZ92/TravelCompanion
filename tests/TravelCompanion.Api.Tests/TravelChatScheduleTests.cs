using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class TravelChatScheduleTests
{
    private static readonly DateOnly Date = new(2026, 10, 6);

    [Fact]
    public async Task Schedule_reads_only_the_selected_trip_and_preserves_stored_reservations()
    {
        await using var db = InMemory();
        await AssertSelectedScheduleAsync(db);
    }

    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Postgres_schedule_reads_only_the_selected_trip_with_parameterized_filters()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = database.Open();
        await AssertSelectedScheduleAsync(db);
        var agendaQuery = Assert.Single(database.Counter.Commands,
            sql => sql.Contains("FROM \"Trips\"") && sql.Contains("\"Reservations\""));
        Assert.Contains("\"AppUserId\"", agendaQuery);
        Assert.Contains("\"IsArchived\"", agendaQuery);
        Assert.DoesNotContain("Gyoza", agendaQuery);
        Assert.Empty(db.ChangeTracker.Entries<Reservation>());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("archived")]
    [InlineData("draft")]
    [InlineData("outside")]
    public async Task Unavailable_selected_trip_does_not_fall_back_to_other_owned_trips(string state)
    {
        await using var db = InMemory();
        var (user, _, other) = await SeedAsync(db);
        var selectedId = other.Id;
        switch (state)
        {
            case "missing": selectedId = Guid.NewGuid(); break;
            case "foreign":
                var foreign = new AppUser { Id = Guid.NewGuid(), Email = "other@example.test", DisplayName = "Other" };
                db.Add(foreign);
                other.AppUserId = foreign.Id;
                break;
            case "archived": other.IsArchived = true; break;
            case "draft": other.PublicationStatus = TripPublicationStatus.Draft; break;
            case "outside": other.EndsOn = Date.AddDays(-1); break;
        }
        await db.SaveChangesAsync();

        var response = await Service(db).CreatePlanAsync(user, Request(), default, selectedId);

        Assert.Equal("date", response.MissingContext?.Field);
        Assert.DoesNotContain("Gyoza", response.Message);
        Assert.DoesNotContain("Machiya", response.Message);
    }

    [Fact]
    public async Task Empty_selected_trip_does_not_use_another_trips_reservations()
    {
        await using var db = InMemory();
        var (user, selected, _) = await SeedAsync(db);
        db.RemoveRange(selected.Reservations);
        selected.Reservations.Clear();
        await db.SaveChangesAsync();

        var response = await Service(db).CreatePlanAsync(user, Request(), default, selected.Id);

        Assert.Null(response.MissingContext);
        Assert.Equal("view_schedule", response.Intent);
        Assert.Contains("no tenes reservas", response.Message);
        Assert.DoesNotContain("Gyoza", response.Message);
    }

    [Theory]
    [InlineData("es-ES", "09:00: Gyoza", "11:00 · Check-out: Machiya")]
    [InlineData("en-US", "09:00: Gyoza", "11:00 · Check-out: Machiya")]
    public async Task Checkout_uses_its_departure_time_and_is_ordered_after_earlier_events(
        string locale, string first, string second)
    {
        await using var db = InMemory();
        var (user, selected, _) = await SeedAsync(db);
        selected.Reservations.Single(item => item.Type == ReservationType.Lodging).EndsOn = Date;
        await db.SaveChangesAsync();

        var response = await Service(db).CreatePlanAsync(user, Request(locale), default, selected.Id);

        Assert.True(response.Message.IndexOf(first, StringComparison.Ordinal)
            < response.Message.IndexOf(second, StringComparison.Ordinal));
        Assert.Contains(second, response.Message);
        Assert.DoesNotContain("15:00", response.Message);
    }

    [Theory]
    [InlineData("es-ES", "En curso: Machiya", "Además, hay una reserva más.")]
    [InlineData("en-US", "Ongoing: Machiya", "There is 1 more reservation.")]
    public async Task Ongoing_stays_do_not_repeat_a_previous_days_check_in_time(string locale, string stay, string extra)
    {
        await using var db = InMemory();
        var (user, selected, _) = await SeedAsync(db);

        var response = await Service(db).CreatePlanAsync(user, Request(locale), default, selected.Id);

        Assert.Contains(stay, response.Message);
        Assert.DoesNotContain("15:00", response.Message);
        Assert.EndsWith("\n" + extra, response.Message);
    }

    [Fact]
    public void Arrival_keeps_the_check_in_time_and_multiday_events_label_their_ending_time()
    {
        var text = new TravelAssistantTextProvider();
        var stay = Reservation("Machiya", 15);
        stay.Type = ReservationType.Lodging;
        stay.EndsOn = Date.AddDays(2);
        Assert.Contains("15:00: Machiya", text.ScheduleSummaryMessage(Date, "Japan", [stay], "es"));
        var eventItem = Reservation("Festival", 15);
        eventItem.Date = Date.AddDays(-1);
        eventItem.EndsOn = Date;
        eventItem.EndsAt = new(18, 0);
        Assert.Contains("18:00 · Finaliza: Festival", text.ScheduleSummaryMessage(Date, "Japan", [eventItem], "es"));
        Assert.Contains("18:00 · Ends: Festival", text.ScheduleSummaryMessage(Date, "Japan", [eventItem], "en"));
    }

    private static async Task AssertSelectedScheduleAsync(TravelCompanionDbContext db)
    {
        var (user, selected, _) = await SeedAsync(db);
        db.ChangeTracker.Clear();

        var response = await Service(db).CreatePlanAsync(user, Request(), default, selected.Id);

        Assert.Null(response.MissingContext);
        Assert.Equal("view_schedule", response.Intent);
        Assert.Equal(1, response.Message.Split("Gyoza", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, response.Message.Split("Machiya", StringSplitOptions.None).Length - 1);
        Assert.Equal(5, response.Message.Split('\n').Count(line => line.StartsWith("- ")));
        Assert.Contains("En curso: Machiya", response.Message);
        Assert.EndsWith("\nAdemás, hay una reserva más.", response.Message);
        Assert.Equal(12, await db.Reservations.CountAsync());
        Assert.Empty(response.Cards);
    }

    private static async Task<(AppUser User, Trip Selected, Trip Other)> SeedAsync(TravelCompanionDbContext db)
    {
        var destination = new Destination
        {
            Id = Guid.NewGuid(), Name = "Japon", Slug = "japan", Country = "Japan", HeroImageUrl = "", ShortDescription = ""
        };
        var user = new AppUser { Id = Guid.NewGuid(), Email = "schedule@example.test", DisplayName = "Schedule" };
        Trip Trip() => new()
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = destination.Id, TravelerName = "Schedule",
            StartsOn = Date.AddDays(-1), EndsOn = Date.AddDays(2), Reservations =
            [Reservation("Machiya", 15), Reservation("Gyoza", 9), Reservation("Eikan", 12),
                Reservation("Tea", 16), Reservation("Dinner", 18), Reservation("Night walk", 20)]
        };
        var selected = Trip();
        var other = Trip();
        foreach (var trip in new[] { selected, other })
        {
            var stay = trip.Reservations[0];
            stay.Type = ReservationType.Lodging;
            stay.Date = Date.AddDays(-1);
            stay.EndsOn = Date.AddDays(1);
            stay.EndsAt = new(11, 0);
        }
        db.AddRange(destination, user, selected, other);
        await db.SaveChangesAsync();
        return (user, selected, other);
    }

    private static Reservation Reservation(string title, int hour) => new()
    {
        Id = Guid.NewGuid(), Date = Date, StartsAt = new(hour, 0), Title = title, City = "Kyoto",
        LocationName = title, Address = "Synthetic", ConfirmationCode = "", Notes = ""
    };

    private static TravelChatRequest Request(string locale = "es-ES") => new("Ver mi agenda", null, "Kyoto", Date, null, locale);
    private static TravelCompanionDbContext InMemory() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static TravelChatService Service(TravelCompanionDbContext db)
    {
        var text = new TravelAssistantTextProvider();
        return new(db, new UserProfileService(db),
            new TravelAssistantActionPlanner(new TravelChatIntentClassifier(),
                new TravelPreferenceCommandParser(new RecommendationTagCatalogService(db))),
            text, new TravelAssistantConversationStateService(db, NullLogger<TravelAssistantConversationStateService>.Instance),
            new TravelChatResponseComposer(text), new TravelRecommendationPlanningService(db, new DeterministicRecommendationRanker()),
            new NoModel(), Microsoft.Extensions.Options.Options.Create(new OpenAiTravelOptions()),
            new TravelAssistantTelemetry(NullLogger<TravelAssistantTelemetry>.Instance), NullLogger<TravelChatService>.Instance);
    }

    private sealed class NoModel : ITravelAiModelClient
    {
        public Task<TravelAiModelResult?> CreateStructuredResponseAsync(TravelAiModelRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("The saved agenda must not need a model.");
    }
}
