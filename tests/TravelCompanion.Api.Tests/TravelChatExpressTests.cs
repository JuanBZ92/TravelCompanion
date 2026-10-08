using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed partial class TravelChatServiceTests
{
    private sealed class ExpressClock(DateTimeOffset? instant = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant ?? new(2026, 10, 8, 0, 45, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly ExpressDate = new(2026, 10, 8);

    private static TravelChatRequest ExpressRequest(Trip trip, Guid? anchor = null) => FreeTimeRequest(trip) with
    {
        Date = ExpressDate,
        Criteria = new(GuidedTravelCategories.Culture, MaxDurationMinutes: 120)
        {
            WindowStartsAtLocal = ExpressDate.ToDateTime(new(10, 0)),
            WindowEndsAtLocal = ExpressDate.ToDateTime(new(12, 0)),
            WindowTimeZoneId = trip.TimeZoneId,
            NearReservationId = anchor,
            MaxWalkingMinutes = anchor.HasValue ? 30 : null
        }
    };

    private static void SetExpressDates(Trip trip)
    {
        trip.StartsOn = ExpressDate;
        trip.EndsOn = ExpressDate.AddDays(5);
    }

    [Fact]
    public async Task Express_next_plan_anchor_is_owned_live_and_separate_from_the_current_position()
    {
        await using var db = CreateDbContext();
        var (user, trip, shortIdea, longIdea) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        longIdea.Latitude += 1;
        var booking = CreateReservation("Lunch by the gallery", new(12, 0), "Tokyo", ExpressDate);
        booking.TripId = trip.Id;
        booking.LocationName = "";
        booking.Latitude = shortIdea.Latitude;
        booking.Longitude = shortIdea.Longitude;
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();
        var service = CreateService(db, timeProvider: new ExpressClock());
        var response = await service.CreatePlanAsync(user, ExpressRequest(trip, booking.Id), CancellationToken.None, trip.Id);
        var card = Assert.Single(response.Cards);
        Assert.Equal(shortIdea.Id.ToString(), card.RecommendationId);
        Assert.Equal("10:10", card.StartTime); // Unknown origin is not treated as the future booking's location.
        Assert.Contains("Near your next plan", card.Subtitle);
        Assert.Contains(booking.Title, card.Subtitle);
        Assert.Contains(card.Warnings, warning => warning.Contains("current position is unknown"));
        Assert.Equal(booking.Id, response.Criteria!.NearReservationId);

        var farOrigin = await service.CreatePlanAsync(user, ExpressRequest(trip, booking.Id) with
        {
            CurrentLocation = new(10, 10)
        }, CancellationToken.None, trip.Id);
        Assert.Empty(farOrigin.Cards); // Ranking near the anchor must not hide the actual outbound transfer.
    }

    [Theory]
    [InlineData("other_account")]
    [InlineData("tomorrow")]
    [InlineData("ended")]
    [InlineData("not_next")]
    [InlineData("without_location")]
    public async Task Express_rejects_a_private_stale_or_unavailable_next_plan(string reason)
    {
        await using var db = CreateDbContext();
        var (user, trip, idea, _) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        var booking = CreateReservation("Next plan", new(12, 0), "Tokyo", ExpressDate);
        booking.TripId = trip.Id;
        booking.Latitude = idea.Latitude;
        booking.Longitude = idea.Longitude;
        if (reason == "other_account") booking.TripId = Guid.NewGuid();
        if (reason == "tomorrow") booking.Date = ExpressDate.AddDays(1);
        if (reason == "ended") { booking.StartsAt = new(8, 0); booking.EndsAt = new(9, 0); }
        if (reason == "without_location") booking.Latitude = null;
        db.Reservations.Add(booking);
        if (reason == "not_next")
        {
            var first = CreateReservation("Earlier plan", new(11, 30), "Tokyo", ExpressDate);
            first.TripId = trip.Id;
            first.Latitude = idea.Latitude;
            first.Longitude = idea.Longitude;
            db.Reservations.Add(first);
        }
        await db.SaveChangesAsync();
        var response = await CreateService(db, timeProvider: new ExpressClock())
            .CreatePlanAsync(user, ExpressRequest(trip, booking.Id), CancellationToken.None, trip.Id);
        Assert.Empty(response.Cards);
        Assert.Equal("area", response.MissingContext?.Field);
        Assert.DoesNotContain(booking.Title, response.Message);
    }

    [Fact]
    public async Task Express_explicit_history_excludes_import_aliases_and_batch_duplicates()
    {
        await using var db = CreateDbContext();
        var (user, trip, original, other) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        original.Title = "Café  de la Galería";
        original.ProviderPlaceId = "place-a";
        other.Title = " cafe-de-la-galeria ";
        other.SuggestedDurationMinutes = 20;
        other.ProviderPlaceId = "place-b";
        var providerAlias = CreateRecommendation(trip.DestinationId, "Gallery alias", "Culture", "Tokyo", 20);
        providerAlias.ProviderPlaceId = "place-b";
        var fresh = CreateRecommendation(trip.DestinationId, "Fresh museum", "Culture", "Tokyo", 20);
        db.Recommendations.AddRange(providerAlias, fresh);
        await db.SaveChangesAsync();
        var service = CreateService(db, timeProvider: new ExpressClock());
        var first = await service.CreatePlanAsync(user, ExpressRequest(trip), CancellationToken.None, trip.Id);
        Assert.Equal(2, first.Cards.Count);
        Assert.Single(first.Cards, card => card.RecommendationId != fresh.Id.ToString());

        var excluded = await service.CreatePlanAsync(user, ExpressRequest(trip) with
        {
            Criteria = ExpressRequest(trip).Criteria! with { ExcludedRecommendationIds = [original.Id] }
        }, CancellationToken.None, trip.Id);
        Assert.Equal(fresh.Id.ToString(), Assert.Single(excluded.Cards).RecommendationId);
    }

    [Fact]
    public async Task Express_individual_replacements_preserve_other_cards_and_complete_explicit_history()
    {
        await using var db = CreateDbContext();
        var (user, trip, firstIdea, secondIdea) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        secondIdea.SuggestedDurationMinutes = 20;
        for (var index = 0; index < 28; index++)
            db.Recommendations.Add(CreateRecommendation(trip.DestinationId, $"Gallery {index:00}", "Culture", "Tokyo", 20));
        await db.SaveChangesAsync();
        var service = CreateService(db, timeProvider: new ExpressClock());
        var initial = await service.CreatePlanAsync(user, ExpressRequest(trip), CancellationToken.None, trip.Id);
        Assert.Equal(2, initial.Cards.Count);
        var retained = initial.Cards[1];
        var target = initial.Cards[0];
        var history = initial.Cards.Select(card => Guid.Parse(card.RecommendationId!)).ToList();
        for (var index = 0; index < 24; index++)
        {
            var replacement = await service.CreatePlanAsync(user, ExpressRequest(trip) with
            {
                ConversationId = initial.ConversationId,
                GuidedAction = new(GuidedTravelActions.Alternative, RecommendationId: target.RecommendationId),
                Criteria = ExpressRequest(trip).Criteria! with { ExcludedRecommendationIds = history.ToArray() }
            }, CancellationToken.None, trip.Id);
            target = Assert.Single(replacement.Cards);
            var id = Guid.Parse(target.RecommendationId!);
            Assert.DoesNotContain(id, history);
            Assert.NotEqual(retained.RecommendationId, target.RecommendationId);
            history.Add(id);
        }
        Assert.Equal(26, history.Distinct().Count()); // Beyond the conversation's legacy 20-item cache.
        var conversation = await db.TravelChatConversations.SingleAsync();
        Assert.True(conversation.LastRecommendationIds!.Length <= 512);
        using var stored = JsonDocument.Parse(conversation.StateJson!);
        Assert.Equal(26, stored.RootElement.GetProperty("lastRecommendationIds").GetArrayLength());
        var usingServerHistory = await service.CreatePlanAsync(user, ExpressRequest(trip) with
        {
            ConversationId = initial.ConversationId,
            GuidedAction = new(GuidedTravelActions.Alternative, RecommendationId: target.RecommendationId)
        }, CancellationToken.None, trip.Id);
        var next = Assert.Single(usingServerHistory.Cards);
        Assert.DoesNotContain(Guid.Parse(next.RecommendationId!), history);
    }

    [Fact]
    public async Task Express_conversation_keeps_optional_window_anchor_and_exclusions_on_a_follow_up()
    {
        await using var db = CreateDbContext();
        var (user, trip, idea, _) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        var booking = CreateReservation("Next plan", new(12, 0), "Tokyo", ExpressDate);
        booking.TripId = trip.Id;
        booking.Latitude = idea.Latitude;
        booking.Longitude = idea.Longitude;
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();
        var service = CreateService(db, timeProvider: new ExpressClock());
        var excluded = Guid.NewGuid();
        var request = ExpressRequest(trip, booking.Id) with
        {
            Criteria = ExpressRequest(trip, booking.Id).Criteria! with { ExcludedRecommendationIds = [excluded] }
        };
        var initial = await service.CreatePlanAsync(user, request, CancellationToken.None, trip.Id);
        var followUp = await service.CreatePlanAsync(user, request with
        {
            ConversationId = initial.ConversationId, Criteria = null
        }, CancellationToken.None, trip.Id);
        Assert.NotEmpty(followUp.Cards);
        Assert.Equal(booking.Id, followUp.Criteria!.NearReservationId);
        Assert.Equal(request.Criteria!.WindowStartsAtLocal, followUp.Criteria.WindowStartsAtLocal);
        Assert.Contains(excluded, followUp.Criteria.ExcludedRecommendationIds!);
    }

    [Fact]
    public async Task Express_explicit_null_exclusions_from_json_are_treated_as_an_empty_history()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        await db.SaveChangesAsync();
        var criteria = JsonSerializer.Deserialize<GuidedPlanCriteriaDto>(
            "{\"category\":\"culture\",\"excludedRecommendationIds\":null}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var response = await CreateService(db, timeProvider: new ExpressClock()).CreatePlanAsync(user,
            ExpressRequest(trip) with { Criteria = criteria with
            {
                WindowStartsAtLocal = ExpressDate.ToDateTime(new(10, 0)),
                WindowEndsAtLocal = ExpressDate.ToDateTime(new(12, 0)),
                WindowTimeZoneId = trip.TimeZoneId
            } }, CancellationToken.None, trip.Id);
        Assert.NotEmpty(response.Cards);
        Assert.Empty(response.Criteria!.ExcludedRecommendationIds!);
    }

    [Fact]
    public async Task Express_next_plan_uses_trip_time_for_an_anchor_in_a_different_reservation_zone()
    {
        await using var db = CreateDbContext();
        var (user, trip, idea, _) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        // 20:00 in Los Angeles on the 7th is noon in Japan on the 8th.
        var booking = CreateReservation("Cross-zone plan", new(20, 0), "Tokyo", ExpressDate.AddDays(-1));
        booking.TimeZoneId = "America/Los_Angeles";
        booking.EndsAt = new(21, 0);
        booking.TripId = trip.Id;
        booking.Latitude = idea.Latitude;
        booking.Longitude = idea.Longitude;
        db.Reservations.Add(booking);
        await db.SaveChangesAsync();
        var response = await CreateService(db, timeProvider: new ExpressClock())
            .CreatePlanAsync(user, ExpressRequest(trip, booking.Id), CancellationToken.None, trip.Id);
        Assert.NotEmpty(response.Cards);
        Assert.Equal(booking.Id, response.Criteria!.NearReservationId);
        var afterMidnight = await CreateService(db,
            timeProvider: new ExpressClock(new(2026, 10, 8, 15, 1, 0, TimeSpan.Zero)))
            .CreatePlanAsync(user, ExpressRequest(trip, booking.Id), CancellationToken.None, trip.Id);
        Assert.Empty(afterMidnight.Cards);
        Assert.Equal("time_window", afterMidnight.MissingContext?.Field);
    }

    [Fact]
    public async Task Express_options_do_not_wait_for_unused_model_text()
    {
        await using var db = CreateDbContext();
        var (user, trip, _, _) = await SeedFreeTimeAsync(db);
        SetExpressDates(trip);
        await db.SaveChangesAsync();
        var model = new CapturingTravelAiModelClient();
        var response = await CreateService(db, model, timeProvider: new ExpressClock())
            .CreatePlanAsync(user, ExpressRequest(trip), CancellationToken.None, trip.Id);
        Assert.NotEmpty(response.Cards);
        Assert.Null(model.LastRequest);
        Assert.All(response.Cards, card => Assert.NotEmpty(card.WhyItFits));
    }
}
