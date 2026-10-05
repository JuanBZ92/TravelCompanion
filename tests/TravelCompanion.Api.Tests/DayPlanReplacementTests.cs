using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class DayPlanReplacementTests
{
    private static TravelCompanionDbContext CreateDb() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseInMemoryDatabase($"day-plan-replacement-{Guid.NewGuid():N}").Options);
    private static DayPlanReplaceRequest Request(DayPlanResponse proposal, DayPlanStopDto target, int revision = 0) =>
        new(proposal.OperationId, proposal.TripId, revision, target.Id, Guid.NewGuid(), "es")
        { ExpectedProposalRevision = proposal.ProposalRevision };

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Replacement_changes_only_one_card_and_excludes_the_entire_visible_proposal(int days)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var generation = world.Request(days);
        var service = DayPlanTestWorld.Service(db);
        var original = await service.GenerateAsync(world.Access(), generation, default);
        var day = original.Days[^1];
        var target = day.Stops[0];
        var result = await service.ReplaceAsync(world.Access(), Request(original, target), default);

        Assert.True(result.Replaced);
        Assert.Equal("replaced", result.Code);
        Assert.Equal(original.OperationId, result.Proposal.OperationId);
        Assert.Equal(1, result.Proposal.ProposalRevision);
        var replacement = result.Proposal.Days[^1].Stops[0];
        Assert.NotEqual(target.Id, replacement.Id);
        Assert.DoesNotContain(original.Days.SelectMany(item => item.Stops), stop => stop.RecommendationId == replacement.RecommendationId);
        Assert.Equal(target.PeriodKey, replacement.PeriodKey);
        Assert.StartsWith(day.Cities[0], replacement.Card.Title);
        Assert.Equal(JsonSerializer.Serialize(original.Days.SelectMany(item => item.Stops).Where(stop => stop.Id != target.Id)),
            JsonSerializer.Serialize(result.Proposal.Days.SelectMany(item => item.Stops).Where(stop => stop.Id != replacement.Id)));
        var current = await service.GenerateAsync(world.Access(), generation, default);
        Assert.Equal(JsonSerializer.Serialize(result.Proposal), JsonSerializer.Serialize(current));
        Assert.Empty(await db.Reservations.ToListAsync());
        Assert.Equal(0, world.Trip.PlanRevision);
        Assert.Single(await db.AssistantUsageLeases.Where(item => item.CompletedAtUtc != null).ToListAsync());
    }

    [Theory]
    [InlineData("morning")]
    [InlineData("midday")]
    [InlineData("afternoon")]
    [InlineData("night")]
    public async Task Replacement_retains_the_requested_moment_and_original_preference_context(string period)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var generation = world.Request() with { Preferences = new("balanced", "medium", ["culture"]) };
        var original = await service.GenerateAsync(world.Access(), generation, default);
        var target = original.Days[0].Stops.Single(stop => stop.PeriodKey == period);
        world.User.TravelPreferenceProfile!.TravelPace = "relaxed";
        world.User.TravelPreferenceProfile.BudgetLevel = "low";
        world.User.TravelPreferenceProfile.Interests = ["shopping"];
        await db.SaveChangesAsync();

        var replaced = await service.ReplaceAsync(world.Access(), Request(original, target), default);

        Assert.True(replaced.Replaced);
        var replacement = replaced.Proposal.Days[0].Stops.Single(stop => stop.PeriodKey == period);
        Assert.Equal(target.Card.StartTime, replacement.Card.StartTime);
        using var persisted = JsonDocument.Parse((await db.AssistantUsageLeases.SingleAsync()).ResponseJson!);
        var preferences = persisted.RootElement.GetProperty("_planningState").GetProperty("Preferences");
        Assert.Equal("balanced", preferences.GetProperty("TravelPace").GetString());
        Assert.Equal("medium", preferences.GetProperty("Budget").GetString());
        Assert.Equal("culture", preferences.GetProperty("Interests")[0].GetString());
    }

    [Fact]
    public async Task Seen_alternatives_never_cycle_and_exhaustion_keeps_the_card_and_a_stable_receipt()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 6);
        var service = DayPlanTestWorld.Service(db);
        var generation = world.Request();
        var proposal = await service.GenerateAsync(world.Access(), generation, default);
        var seen = proposal.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId).ToHashSet();
        DayPlanReplaceResponse? first = null;
        DayPlanReplaceRequest? firstRequest = null;
        DayPlanReplaceResponse? exhausted = null;
        for (var index = 0; index < 8; index++)
        {
            var request = Request(proposal, proposal.Days[0].Stops[0]);
            var result = await service.ReplaceAsync(world.Access(), request, default);
            if (!result.Replaced)
            {
                exhausted = result;
                Assert.Equal("no_alternative", result.Code);
                Assert.Equal(JsonSerializer.Serialize(proposal), JsonSerializer.Serialize(result.Proposal));
                var replay = await service.ReplaceAsync(world.Access(), request, default);
                Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
                break;
            }
            first ??= result;
            firstRequest ??= request;
            proposal = result.Proposal;
            Assert.True(seen.Add(proposal.Days[0].Stops[0].RecommendationId));
        }
        Assert.NotNull(first);
        Assert.NotNull(exhausted);
        Assert.Equal(6, seen.Count);
        var oldReplay = await service.ReplaceAsync(world.Access(), firstRequest!, default);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(oldReplay));
        var current = await service.GenerateAsync(world.Access(), generation, default);
        Assert.Equal(JsonSerializer.Serialize(proposal), JsonSerializer.Serialize(current));
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Trial_replacements_remain_available_after_three_generations_without_consuming_another_use()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        var service = DayPlanTestWorld.Service(db);
        var first = await service.GenerateAsync(world.Access(), world.Request(3), default);
        await service.GenerateAsync(world.Access(), world.Request(), default);
        await service.GenerateAsync(world.Access(), world.Request(), default);

        var result = await service.ReplaceAsync(world.Access(), Request(first, first.Days[0].Stops[0]), default);

        Assert.True(result.Replaced);
        Assert.Equal(0, result.Proposal.TrialAccess!.DayImprovementsRemaining);
        Assert.Equal(3, await db.AssistantUsageLeases.CountAsync(item => item.CompletedAtUtc != null));
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Current_dietary_restrictions_and_access_are_rechecked_for_the_alternative()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, trial: true);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var ids = proposal.Days.SelectMany(day => day.Stops).Select(stop => stop.RecommendationId).ToHashSet();
        var available = await db.Recommendations.Where(item => item.Category == "Food"
            && item.Neighborhood.Contains("Tokyo") && !ids.Contains(item.Id)).ToListAsync();
        var allowed = available[0];
        allowed.Tags.Add("vegan");
        var locked = available[1];
        locked.Tags.Add("vegan");
        locked.AccessLevel = ContentAccessLevel.Paid;
        var distant = available[2];
        distant.Tags.Add("vegan");
        distant.Latitude = 34;
        distant.Longitude = 137;
        world.User.TravelPreferenceProfile!.DietaryRestrictions = ["vegan"];
        await db.SaveChangesAsync();

        var result = await service.ReplaceAsync(world.Access(), Request(proposal,
            proposal.Days[0].Stops.Single(stop => stop.PeriodKey == "midday")), default);

        Assert.True(result.Replaced);
        Assert.Equal(allowed.Id, result.Proposal.Days[0].Stops.Single(stop => stop.PeriodKey == "midday").RecommendationId);
        Assert.Equal(2, result.Proposal.TrialAccess!.DayImprovementsRemaining);
    }

    [Fact]
    public async Task Duplicate_catalog_titles_and_provider_places_cannot_repeat_a_visible_or_discarded_place()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 4);
        foreach (var item in await db.Recommendations.ToListAsync()) item.ProviderPlaceId = $"synthetic:{item.Id:N}";
        await db.SaveChangesAsync();
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var target = proposal.Days[0].Stops[0];
        var byTitle = Alternative(world, "  " + target.Card.Title.ToUpperInvariant() + "  ");
        var byPlace = Alternative(world, "Tokyo different catalog title");
        byPlace.ProviderPlaceId = target.Card.ProviderPlaceId;
        db.AddRange(byTitle, byPlace);
        await db.SaveChangesAsync();

        var result = await service.ReplaceAsync(world.Access(), Request(proposal, target), default);

        Assert.False(result.Replaced);
        Assert.Equal("no_alternative", result.Code);
        Assert.Equal(target.Id, result.Proposal.Days[0].Stops[0].Id);
    }

    [Fact]
    public async Task Initial_multi_day_proposal_never_repeats_catalog_aliases_with_different_ids()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 6);
        var originals = await db.Recommendations.Where(item => item.Neighborhood.Contains("Tokyo")).ToListAsync();
        foreach (var original in originals)
        {
            original.ProviderPlaceId = $"synthetic:{original.Id:N}";
            var byTitle = Alternative(world, " " + original.Title.ToUpperInvariant() + " ");
            var byProvider = Alternative(world, original.Title + " alternate catalog name");
            byTitle.Category = byProvider.Category = original.Category;
            byTitle.Tags = original.Tags.ToList();
            byProvider.Tags = original.Tags.ToList();
            byProvider.ProviderPlaceId = " " + original.ProviderPlaceId + " ";
            db.AddRange(byTitle, byProvider);
        }
        await db.SaveChangesAsync();

        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(3), default);

        var stops = proposal.Days.SelectMany(day => day.Stops).ToList();
        Assert.Equal(6, stops.Count);
        Assert.Equal(stops.Count, stops.Select(stop => stop.Card.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var recommendedIds = stops.Select(stop => stop.RecommendationId).ToHashSet();
        foreach (var original in originals)
            Assert.Single(await db.Recommendations.Where(item => recommendedIds.Contains(item.Id)
                && (item.Title.Trim().ToLower() == original.Title.Trim().ToLower()
                    || item.ProviderPlaceId != null && item.ProviderPlaceId.Trim() == original.ProviderPlaceId)).ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_provider_ids_do_not_exhaust_unrelated_alternatives_after_metadata_is_reloaded(string providerId)
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        foreach (var item in await db.Recommendations.ToListAsync()) item.ProviderPlaceId = providerId;
        await db.SaveChangesAsync();
        var proposal = await DayPlanTestWorld.Service(db).GenerateAsync(world.Access(), world.Request(), default);
        db.ChangeTracker.Clear();

        var first = await DayPlanTestWorld.Service(db).ReplaceAsync(world.Access(), Request(proposal, proposal.Days[0].Stops[0]), default);
        Assert.True(first.Replaced);
        db.ChangeTracker.Clear();
        var next = await DayPlanTestWorld.Service(db).ReplaceAsync(world.Access(), Request(first.Proposal,
            first.Proposal.Days[0].Stops[0]), default);

        Assert.True(next.Replaced);
        Assert.NotEqual(first.Proposal.Days[0].Stops[0].RecommendationId, next.Proposal.Days[0].Stops[0].RecommendationId);
        using var state = JsonDocument.Parse((await db.AssistantUsageLeases.SingleAsync()).ResponseJson!);
        Assert.Empty(state.RootElement.GetProperty("_planningState").GetProperty("SeenProviderPlaceIds").EnumerateArray());
    }

    [Fact]
    public async Task A_no_alternative_receipt_remains_stable_after_another_card_is_replaced()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db, perCity: 4);
        var service = DayPlanTestWorld.Service(db);
        var generation = world.Request();
        var proposal = await service.GenerateAsync(world.Access(), generation, default);
        var exhaustedRequest = Request(proposal, proposal.Days[0].Stops[0]);
        var exhausted = await service.ReplaceAsync(world.Access(), exhaustedRequest, default);
        Assert.False(exhausted.Replaced);
        var available = Alternative(world, "Tokyo newly available museum");
        db.Add(available);
        await db.SaveChangesAsync();
        var another = await service.ReplaceAsync(world.Access(), Request(proposal,
            proposal.Days[0].Stops.Single(stop => stop.PeriodKey == "afternoon")), default);
        Assert.True(another.Replaced);

        var replay = await service.ReplaceAsync(world.Access(), exhaustedRequest, default);

        Assert.Equal(JsonSerializer.Serialize(exhausted), JsonSerializer.Serialize(replay));
        var current = await service.GenerateAsync(world.Access(), generation, default);
        Assert.Equal(1, current.ProposalRevision);
        Assert.Contains(current.Days[0].Stops, stop => stop.RecommendationId == available.Id);
    }

    [Fact]
    public async Task Saved_cards_cannot_be_replaced_even_after_the_saved_reservation_is_deleted()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var target = proposal.Days[0].Stops[0];
        var saved = await service.ApplyAsync(world.Access(), new(proposal.OperationId, world.Trip.Id, 0,
            Guid.NewGuid(), [target.Id]), default);
        db.Remove(await db.Reservations.SingleAsync());
        await db.SaveChangesAsync();

        var rejected = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(),
            Request(proposal, target, saved.Revision), default));

        Assert.Equal("selection", rejected.Code);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    private static Recommendation Alternative(DayPlanTestWorld world, string title) => new()
    {
        Id = Guid.NewGuid(), DestinationId = world.Trip.DestinationId, Title = title, Category = "Culture",
        Neighborhood = "Tokyo, Japan", Description = "Synthetic Tokyo place", Tags = ["culture", "museum"],
        PriceLevel = "medium", Latitude = 35.665m, Longitude = 139.77m, SuggestedDurationMinutes = 60,
        OpeningHours = "08:00-23:00", Rating = 4.5, AccessLevel = ContentAccessLevel.Free
    };

    [Fact]
    public async Task Legacy_proposals_require_the_exact_original_request_to_restore_temporary_preferences()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var generation = world.Request(pace: "efficient");
        var proposal = await service.GenerateAsync(world.Access(), generation, default);
        var lease = await db.AssistantUsageLeases.SingleAsync();
        lease.ResponseJson = JsonSerializer.Serialize(proposal);
        await db.SaveChangesAsync();
        var request = Request(proposal, proposal.Days[0].Stops[0]);
        var missing = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(), request, default));
        Assert.Equal("operation", missing.Code);
        var invalid = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(), request with
        { OriginalRequest = generation with { Preferences = new("relaxed", "low", []) } }, default));
        Assert.Equal("operation", invalid.Code);

        var result = await service.ReplaceAsync(world.Access(), request with { OriginalRequest = generation }, default);

        Assert.True(result.Replaced);
        Assert.Equal(5, result.Proposal.Days[0].Stops.Count);
        Assert.Single(await db.AssistantUsageLeases.ToListAsync());
    }

    [Fact]
    public async Task Old_selection_is_rejected_and_only_the_persisted_replacement_can_be_applied()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var target = proposal.Days[0].Stops[0];
        var replacement = await service.ReplaceAsync(world.Access(), Request(proposal, target), default);
        var old = new DayPlanApplyRequest(proposal.OperationId, world.Trip.Id, 0, Guid.NewGuid(),
            [target.Id, proposal.Days[0].Stops[1].Id]);

        var stale = await Assert.ThrowsAsync<DayPlanException>(() => service.ApplyAsync(world.Access(), old, default));
        Assert.Equal("selection", stale.Code);
        Assert.Empty(await db.Reservations.ToListAsync());
        var current = replacement.Proposal.Days[0].Stops[0];
        var saved = await service.ApplyAsync(world.Access(), old with
        { MutationId = Guid.NewGuid(), SelectedStopIds = [current.Id, proposal.Days[0].Stops[1].Id] }, default);
        Assert.Equal(2, saved.Items.Count);
        Assert.Contains(saved.Items, item => item.RecommendationId == current.RecommendationId);
        Assert.DoesNotContain(saved.Items, item => item.RecommendationId == target.RecommendationId);
        var remaining = replacement.Proposal.Days[0].Stops[2];
        var another = await service.ReplaceAsync(world.Access(), Request(replacement.Proposal, remaining, saved.Revision), default);
        Assert.True(another.Replaced);
        Assert.Equal(2, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Reusing_mutations_or_stale_proposal_revisions_is_rejected_without_more_changes()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var request = Request(proposal, proposal.Days[0].Stops[0]);
        var result = await service.ReplaceAsync(world.Access(), request, default);
        var changed = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(), request with
        { StopId = proposal.Days[0].Stops[1].Id }, default));
        Assert.Equal("operation", changed.Code);
        var outdated = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(), request with
        { StopId = proposal.Days[0].Stops[1].Id, MutationId = Guid.NewGuid() }, default));
        Assert.Equal("stale", outdated.Code);
        Assert.Equal(1, result.Proposal.ProposalRevision);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Changed_city_and_other_account_or_session_cannot_replace_a_proposal()
    {
        await using var db = CreateDb();
        var world = await DayPlanTestWorld.SeedAsync(db);
        var service = DayPlanTestWorld.Service(db);
        var proposal = await service.GenerateAsync(world.Access(), world.Request(), default);
        var request = Request(proposal, proposal.Days[0].Stops[0]);
        await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(sessionTrip: Guid.NewGuid()), request, default));
        await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(SessionAccessMode.BuilderReadOnly), request, default));
        var other = await DayPlanTestWorld.SeedAsync(db);
        await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(other.Access(), request, default));
        world.Trip.BuilderSegmentsJson = JsonSerializer.Serialize(new BuilderTripSetupSegmentDto[]
        { new("Kyoto", world.Trip.StartsOn, world.Trip.EndsOn) });
        world.Trip.PlanRevision++;
        await db.SaveChangesAsync();
        var moved = await Assert.ThrowsAsync<DayPlanException>(() => service.ReplaceAsync(world.Access(), request with
        { ExpectedRevision = world.Trip.PlanRevision }, default));
        Assert.Equal("stale", moved.Code);
        Assert.Empty(await db.Reservations.ToListAsync());
    }
}
