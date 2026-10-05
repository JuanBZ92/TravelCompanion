using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class DayPlanServiceTests
{
    private static TravelCompanionDbContext CreateDb() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseInMemoryDatabase($"day-plans-{Guid.NewGuid():N}").Options);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Generation_groups_consecutive_days_and_never_repeats_or_writes_itinerary(int count)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, existing: 25);
        var excluded = await db.Recommendations.Where(item => item.Neighborhood.Contains("Tokyo")).FirstAsync();
        var existing = await db.Reservations.FirstAsync();
        existing.RecommendationId = excluded.Id;
        await db.SaveChangesAsync();
        var response = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(count), default);

        Assert.Equal(count, response.Days.Count);
        Assert.Equal(Enumerable.Range(0, count).Select(DayPlanTestWorld.Start.AddDays), response.Days.Select(day => day.Date));
        Assert.All(response.Days, day =>
        {
            Assert.Equal(4, day.Stops.Count);
            var city = day.Date < DayPlanTestWorld.Start.AddDays(4) ? "Tokyo" : "Kyoto";
            Assert.Contains(city, day.Cities);
            Assert.All(day.Stops, stop => Assert.StartsWith(city, stop.Card.Title));
            Assert.All(day.Stops, stop => Assert.Equal($"{city}, Japan", stop.Place));
        });
        var stops = response.Days.SelectMany(day => day.Stops).ToList();
        Assert.Equal(stops.Count, stops.Select(stop => stop.RecommendationId).Distinct().Count());
        Assert.Equal(stops.Count, stops.Select(stop => stop.Id).Distinct().Count());
        Assert.DoesNotContain(stops, stop => stop.RecommendationId == excluded.Id);
        Assert.Equal(25, await db.Reservations.CountAsync());
        Assert.Equal(0, (await db.Trips.SingleAsync()).PlanRevision);
        Assert.Single(await db.AssistantUsageLeases.Where(lease => lease.CompletedAtUtc != null).ToListAsync());
    }

    [Theory]
    [InlineData("en", 1, 1, "1 idea for 1 day. Choose what to add.")]
    [InlineData("en", 1, 60, "4 ideas for 1 day. Choose what to add.")]
    [InlineData("en", 3, 1, "1 idea for 3 days. Choose what to add.")]
    [InlineData("en", 3, 60, "12 ideas for 3 days. Choose what to add.")]
    [InlineData("es", 1, 1, "1 idea para 1 día. Elegí qué añadir.")]
    [InlineData("es", 1, 60, "4 ideas para 1 día. Elegí qué añadir.")]
    [InlineData("es", 3, 1, "1 idea para 3 días. Elegí qué añadir.")]
    [InlineData("es", 3, 60, "12 ideas para 3 días. Elegí qué añadir.")]
    public async Task Proposal_summary_localizes_singular_and_plural_ideas_and_days(
        string locale, int days, int perCity, string expected)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: perCity);

        var response = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(),
            world.Request(days) with { Locale = locale }, default);

        Assert.Equal(expected, response.Message);
    }

    [Fact]
    public async Task Options_show_all_configured_city_days_before_generation_even_without_itinerary_items()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);

        var options = await service.OptionsAsync(world.Access(), default);

        Assert.True(options.Enabled);
        Assert.Equal(7, options.CityDays.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(DayPlanTestWorld.Start.AddDays), options.CityDays.Select(day => day.Date));
        Assert.All(options.CityDays.Take(4), day => Assert.Equal(["Tokyo"], day.Cities));
        Assert.All(options.CityDays.Skip(4), day => Assert.Equal(["Kyoto"], day.Cities));
        Assert.Empty(await db.Reservations.ToListAsync());
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
        var generated = await service.GenerateAsync(world.Access(), world.Request(7), default);
        Assert.All(generated.Days, day => Assert.Equal(
            options.CityDays.Single(context => context.Date == day.Date).Cities, day.Cities));
    }

    [Fact]
    public async Task Options_and_generation_share_both_cities_for_a_transfer_day()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var transfer = DayPlanTestWorld.Start.AddDays(3);
        world.Trip.BuilderSegmentsJson = JsonSerializer.Serialize(new BuilderTripSetupSegmentDto[]
        {
            new("Tokyo", DayPlanTestWorld.Start, transfer),
            new("Kyoto", transfer, world.Trip.EndsOn)
        });
        await db.SaveChangesAsync();
        var service = DayPlanTestWorld.Service(db);

        var options = await service.OptionsAsync(world.Access(), default);
        var generated = await service.GenerateAsync(world.Access(), world.Request() with { StartDate = transfer }, default);

        Assert.Equal(["Tokyo", "Kyoto"], options.CityDays.Single(day => day.Date == transfer).Cities);
        Assert.Equal(options.CityDays.Single(day => day.Date == transfer).Cities, Assert.Single(generated.Days).Cities);
    }

    [Fact]
    public async Task Legacy_city_days_use_an_ongoing_reservation_and_then_the_destination_fallback()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 0);
        world.Trip.BuilderSegmentsJson = null;
        db.Reservations.Add(new()
        {
            Id = Guid.NewGuid(), TripId = world.Trip.Id, Type = ReservationType.Lodging,
            Date = DayPlanTestWorld.Start, EndsOn = DayPlanTestWorld.Start.AddDays(2),
            City = "Osaka", Title = "Synthetic hotel", LocationName = "Hotel", Address = "",
            ConfirmationCode = "", Notes = ""
        });
        await db.SaveChangesAsync();
        var service = DayPlanTestWorld.Service(db);

        var options = await service.OptionsAsync(world.Access(), default);
        var generated = await service.GenerateAsync(world.Access(), world.Request(7), default);

        Assert.All(options.CityDays.Take(3), day => Assert.Equal(["Osaka"], day.Cities));
        Assert.All(options.CityDays.Skip(3), day => Assert.Equal(["Japan"], day.Cities));
        Assert.All(generated.Days, day => Assert.Equal(
            options.CityDays.Single(context => context.Date == day.Date).Cities, day.Cities));
    }

    [Fact]
    public async Task Proposal_place_uses_day_city_when_the_catalog_has_no_neighborhood()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        foreach (var recommendation in await db.Recommendations.ToListAsync())
            recommendation.Neighborhood = " ";
        await db.SaveChangesAsync();

        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(), default);

        var day = Assert.Single(proposal.Days);
        Assert.Equal(4, day.Stops.Count);
        Assert.All(day.Stops, stop => Assert.Equal("Tokyo", stop.Place));
        Assert.All(day.Stops, stop => Assert.NotEqual(stop.Card.Subtitle, stop.Place));
    }

    [Theory]
    [InlineData("relaxed", 3)]
    [InlineData("balanced", 4)]
    [InlineData("efficient", 5)]
    public async Task Pace_changes_new_idea_density_even_when_the_day_contains_many_plans(string pace, int expected)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, existing: 40);
        var result = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(pace: pace), default);
        Assert.Equal(expected, Assert.Single(result.Days).Stops.Count);
        Assert.Equal(40, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Sparse_catalog_reports_missing_moments_without_duplicates_or_invented_places()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 1);
        var result = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(3), default);
        var stops = result.Days.SelectMany(day => day.Stops).ToList();
        Assert.Single(stops);
        Assert.All(result.Days, day => Assert.NotEmpty(day.MissingMoments));
        Assert.Contains(await db.Recommendations.Select(item => item.Id).ToListAsync(), id => id == stops[0].RecommendationId);
    }

    [Fact]
    public async Task Replaying_generation_returns_exact_stable_result_and_rejects_changed_input()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var request = world.Request(3);
        var first = await service.GenerateAsync(world.Access(), request, default);
        var repeated = await service.GenerateAsync(world.Access(), request, default);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(repeated));
        Assert.Single(await db.AssistantUsageLeases.ToListAsync());
        var conflict = await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(world.Access(),
            request with { DayCount = 1 }, default));
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Trial_has_three_generations_including_preferences_and_one_use_for_three_days()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        var service = DayPlanTestWorld.Service(db);
        for (var i = 0; i < 3; i++)
        {
            var result = await service.GenerateAsync(world.Access(), world.Request(3, "efficient"), default);
            Assert.Equal(3, result.Days.Count);
            Assert.All(result.Days, day => Assert.Equal(5, day.Stops.Count));
            Assert.Equal(2 - i, result.TrialAccess!.DayImprovementsRemaining);
        }
        var denied = await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(world.Access(), world.Request(), default));
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(3, await db.AssistantUsageLeases.CountAsync(lease => lease.CompletedAtUtc != null));
        Assert.Null((await db.AppUsers.SingleAsync()).PersonalizedDayTrialUsedAtUtc);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(7, 0)]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public async Task Trial_rejects_paid_ranges_without_consuming_generation(int days, int offset)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        var error = await Assert.ThrowsAsync<DayPlanException>(() => DayPlanTestWorld.Service(db).GenerateAsync(world.Access(),
            world.Request(days) with { StartDate = DayPlanTestWorld.Start.AddDays(offset) }, default));
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Invalid_durations_do_not_reserve_quota(int days)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var error = await Assert.ThrowsAsync<DayPlanException>(() => DayPlanTestWorld.Service(db).GenerateAsync(world.Access(),
            world.Request(days), default));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
    }

    [Fact]
    public async Task Empty_catalog_does_not_consume_generation()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true, perCity: 0);
        var result = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(), default);
        Assert.All(result.Days, day => Assert.Empty(day.Stops));
        Assert.Empty(await db.AssistantUsageLeases.Where(lease => lease.CompletedAtUtc != null).ToListAsync());
    }

    [Theory]
    [InlineData(SessionAccessMode.BuilderReadOnly)]
    [InlineData(SessionAccessMode.Trip)]
    public async Task Read_only_session_cannot_generate_or_apply(SessionAccessMode mode)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var error = await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(world.Access(mode), world.Request(), default));
        Assert.Equal(403, error.StatusCode);
        var apply = new DayPlanApplyRequest(Guid.NewGuid(), world.Trip.Id, 0, Guid.NewGuid(), [Guid.NewGuid()]);
        var applyError = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(mode), apply, default));
        Assert.Equal(403, applyError.StatusCode);
    }

    [Fact]
    public async Task Other_account_and_other_session_trip_cannot_read_or_apply_proposal()
    {
        await using var db = CreateDb();
        var owner = await DayPlanTestWorld.SeedAsync(db);
        var other = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var request = owner.Request();
        var proposal = await service.GenerateAsync(owner.Access(), request, default);
        await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(other.Access(), request, default));
        await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(owner.Access(sessionTrip: other.Trip.Id), request, default));
        await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(other.Access(),
            new(proposal.OperationId, owner.Trip.Id, 0, Guid.NewGuid(), [proposal.Days[0].Stops[0].Id]), default));
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Apply_selected_ideas_once_and_conflicts_preserve_all_existing_plans()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, existing: 12);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(3), default);
        var selected = proposal.Days.Select(day => day.Stops[0]).Select(stop => stop.Id).ToList();
        var request = new DayPlanApplyRequest(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), selected);
        var saved = await service.ApplyAsync(world.Access(), request, default);
        Assert.True(saved.Applied);
        Assert.Equal(1, saved.Revision);
        Assert.Equal(3, saved.Items.Count);
        Assert.All(saved.Items, item => Assert.Equal(ItineraryFlexibility.Flexible, item.Flexibility));
        Assert.Equal(15, await db.Reservations.CountAsync());
        var replay = await service.ApplyAsync(world.Access(), request, default);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(replay));
        Assert.Equal(15, await db.Reservations.CountAsync());
        var stale = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(),
            request with { MutationId = Guid.NewGuid() }, default));
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal(15, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Saved_dietary_rules_free_radius_and_content_permissions_are_applied_to_every_day()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        world.User.TravelPreferenceProfile!.DietaryRestrictions = ["vegan"];
        var catalog = await db.Recommendations.ToListAsync();
        foreach (var food in catalog.Where(item => item.Category == "Food")) food.Tags.Add("vegan");
        var outside = catalog.First(item => item.Category == "Food");
        outside.Latitude = 0;
        outside.Longitude = 0;
        var denied = catalog.First(item => item.Category == "Culture");
        denied.AccessLevel = ContentAccessLevel.AdminOnly;
        var unproven = catalog.Last(item => item.Category == "Food");
        unproven.Tags.Remove("vegan");
        await db.SaveChangesAsync();
        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(3), default);
        var stops = proposal.Days.SelectMany(day => day.Stops).ToList();
        Assert.NotEmpty(stops);
        Assert.DoesNotContain(stops, stop => stop.RecommendationId == outside.Id || stop.RecommendationId == denied.Id || stop.RecommendationId == unproven.Id);
        Assert.All(stops.Where(stop => catalog.Single(item => item.Id == stop.RecommendationId).Category == "Food"),
            stop => Assert.Contains("vegan", catalog.Single(item => item.Id == stop.RecommendationId).Tags));
    }

    [Fact]
    public async Task Missing_profile_uses_defaults_without_creating_or_changing_preferences()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        db.TravelPreferenceProfiles.Remove(world.User.TravelPreferenceProfile!);
        await db.SaveChangesAsync();
        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request() with { Preferences = null }, default);
        Assert.Equal(4, Assert.Single(proposal.Days).Stops.Count);
        Assert.Empty(await db.TravelPreferenceProfiles.ToListAsync());
    }

    [Fact]
    public async Task Apply_rechecks_revoked_access_and_cannot_write_from_a_stale_session()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        world.Grant.RevokedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(),
            new(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), [proposal.Days[0].Stops[0].Id]), default));
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task A_cancelled_request_does_not_consume_a_generation_or_change_the_itinerary()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DayPlanTestWorld.Service(db)
            .GenerateAsync(world.Access(), world.Request(), cancel.Token));
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Generation_and_apply_retries_record_each_existing_conversion_signal_once()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        world.User.BehaviorAnalyticsConsent = true;
        await db.SaveChangesAsync();
        var service = DayPlanTestWorld.Service(db, analytics: true);
        var request = world.Request();
        var proposal = await service.GenerateAsync(world.Access(), request, default);
        await service.GenerateAsync(world.Access(), request, default);
        var apply = new DayPlanApplyRequest(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), [proposal.Days[0].Stops[0].Id]);
        await service.ApplyAsync(world.Access(), apply, default);
        await service.ApplyAsync(world.Access(), apply, default);
        Assert.Single(await db.ProductAnalyticsEvents.Where(item => item.Name == "first_useful_response").ToListAsync());
        Assert.Single(await db.ProductAnalyticsEvents.Where(item => item.Name == "first_item_saved").ToListAsync());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Planner_signals_preserve_consent_and_demo_internal_exclusions(bool consent, bool demo, bool internalUser)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        world.User.BehaviorAnalyticsConsent = consent;
        world.User.IsDemo = demo;
        world.User.IsInternal = internalUser;
        await db.SaveChangesAsync();
        var service = DayPlanTestWorld.Service(db, analytics: true);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        await service.ApplyAsync(world.Access(), new(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), [proposal.Days[0].Stops[0].Id]), default);
        Assert.Empty(await db.ProductAnalyticsEvents.ToListAsync());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Stale_builder_session_cannot_bypass_the_actual_trial_grant(int days)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        var service = DayPlanTestWorld.Service(db);
        var staleAccess = world.Access(SessionAccessMode.Builder);
        var options = await Assert.ThrowsAsync<DayPlanException>(() => service.OptionsAsync(staleAccess, default));
        Assert.Equal(403, options.StatusCode);
        var generate = await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(staleAccess, world.Request(days), default));
        Assert.Equal(403, generate.StatusCode);
        Assert.Equal("access", generate.Code);
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("unpublished")]
    [InlineData("deleted-account")]
    public async Task Revoked_trip_or_account_blocks_even_persisted_replays_from_a_stale_session(string change)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var staleAccess = (world with
        {
            User = new() { Id = world.User.Id, Email = world.User.Email, DisplayName = world.User.DisplayName }
        }).Access();
        var service = DayPlanTestWorld.Service(db);
        var request = world.Request();
        var proposal = await service.GenerateAsync(staleAccess, request, default);
        var apply = new DayPlanApplyRequest(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(), [proposal.Days[0].Stops[0].Id]);
        await service.ApplyAsync(staleAccess, apply, default);
        if (change == "archived") world.Trip.IsArchived = true;
        else if (change == "unpublished") world.Trip.PublicationStatus = TripPublicationStatus.Draft;
        else world.User.DeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var generate = await Assert.ThrowsAsync<DayPlanException>(() => service.GenerateAsync(staleAccess, request, default));
        Assert.Equal(403, generate.StatusCode);
        var replay = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(staleAccess, apply, default));
        Assert.Equal(403, replay.StatusCode);
        if (change == "deleted-account")
            Assert.Equal(403, (await Assert.ThrowsAsync<DayPlanException>(() => service.OptionsAsync(staleAccess, default))).StatusCode);
        else
            Assert.False((await service.OptionsAsync(staleAccess, default)).Enabled);
        Assert.Single(await db.AssistantUsageLeases.ToListAsync());
        Assert.Single(await db.Reservations.ToListAsync());
        Assert.Single(await db.PlanningApplicationReceipts.ToListAsync());
    }
}
