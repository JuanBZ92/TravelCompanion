using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class TripPlanEditorServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task List_limits_each_page_to_fifty_with_stable_order_and_total_counts()
    {
        await using var db = CreateDbContext();
        var destination = await SeedDestinationAsync(db);
        db.Trips.AddRange(Enumerable.Range(0, 101).Select(index => new Trip
        {
            Id = Guid.NewGuid(), DestinationId = destination.Id, TravelerName = "Same client",
            StartsOn = new DateOnly(2026, 10, 1), EndsOn = new DateOnly(2026, 10, 2)
        }));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.ListTripsAsync(null, 1, null);
        var second = await service.ListTripsAsync(null, 2, null);
        var last = await service.ListTripsAsync(null, int.MaxValue, null);

        Assert.Equal(101, first.TotalCount);
        Assert.Equal(101, first.FilteredCount);
        Assert.Equal(50, first.Items.Count);
        Assert.Equal(50, second.Items.Count);
        Assert.Single(last.Items);
        Assert.Equal(3, last.Page);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.Equal(101, first.Items.Concat(second.Items).Concat(last.Items).Select(item => item.Id).Distinct().Count());
        Assert.Equal(first.Items.Select(item => item.Id), (await service.ListTripsAsync(null, -1, null)).Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("draft", 2)]
    [InlineData("published", 1)]
    public async Task List_applies_search_and_status_before_paging_and_keeps_total_count(string status, int expected)
    {
        await using var db = CreateDbContext();
        var destination = await SeedDestinationAsync(db);
        var publishedWithDraft = new Trip { Id = Guid.NewGuid(), DestinationId = destination.Id, TravelerName = "Tokyo client",
            StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 2), PublicationStatus = TripPublicationStatus.Published };
        publishedWithDraft.PlanDraft = new() { TripId = publishedWithDraft.Id, PayloadJson = "{}" };
        db.Trips.AddRange(publishedWithDraft,
            new Trip { Id = Guid.NewGuid(), DestinationId = destination.Id, TravelerName = "Tokyo draft", StartsOn = new(2026, 10, 1),
                EndsOn = new(2026, 10, 2), PublicationStatus = TripPublicationStatus.Draft },
            new Trip { Id = Guid.NewGuid(), DestinationId = destination.Id, TravelerName = "Osaka", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 2) });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListTripsAsync("  TOKYO  ", 8, status);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(expected, result.FilteredCount);
        Assert.Equal(expected, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.All(result.Items, item => Assert.True(item.HasDraft));
    }

    [Fact]
    public async Task Invalid_publish_stays_on_editor_with_submitted_content_without_publishing()
    {
        await using var db = CreateDbContext();
        var destination = await SeedDestinationAsync(db);
        var service = CreateService(db);
        var id = await service.CreateTripAsync(new("Client", "731095", destination.Id, new(2026, 10, 1), new(2026, 10, 1), "Asia/Tokyo"));
        var editor = (await service.GetEditorAsync(id))!;
        editor.Payload.TravelerName = "Edited but not saved";
        var json = JsonSerializer.Serialize(editor.Payload, JsonOptions);
        var page = new TravelCompanion.Api.Pages.Admin.TripsModel(db, service)
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
            TripId = id, DraftJson = json, BasePlanRevision = 0, NewAccessPin = "abc"
        };
        page.ModelState.AddModelError(nameof(page.NewAccessPin), "El PIN debe tener exactamente 6 números.");

        var result = await page.OnPostPublishAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Equal(json, page.DraftJson);
        Assert.Contains("Edited but not saved", page.EditorStateJson);
        Assert.Equal(TripPublicationStatus.Draft, (await db.Trips.SingleAsync()).PublicationStatus);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Invalid_create_dates_keep_the_form_and_report_each_affected_field_without_creating_a_trip(
        bool invalidStart, bool invalidEnd)
    {
        await using var db = CreateDbContext();
        var destination = await SeedDestinationAsync(db);
        var page = new TravelCompanion.Api.Pages.Admin.TripsModel(db, CreateService(db))
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
            CreateInput = new()
            {
                TravelerName = "Unsaved client", AccessPin = "731095", DestinationId = destination.Id,
                CitySegments = [new() { City = "Tokyo", HotelBase = "Unsaved hotel",
                    StartsOn = invalidStart ? default : new(2026, 10, 1),
                    EndsOn = invalidEnd ? default : new(2026, 10, 3) }]
            }
        };
        const string startKey = "CreateInput.CitySegments[0].StartsOn";
        const string endKey = "CreateInput.CitySegments[0].EndsOn";
        if (invalidStart)
        {
            page.ModelState.SetModelValue(startKey, "invalid-date", "invalid-date");
            page.ModelState.AddModelError(startKey, "The value is invalid.");
        }
        if (invalidEnd)
        {
            page.ModelState.SetModelValue(endKey, "", "");
            page.ModelState.AddModelError(endKey, "The value is invalid.");
        }

        var result = await page.OnPostCreateAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Equal("Unsaved client", page.CreateInput.TravelerName);
        Assert.Equal("Unsaved hotel", Assert.Single(page.CreateInput.CitySegments).HotelBase);
        if (invalidStart)
        {
            Assert.Equal("Indica una fecha de inicio válida.", Assert.Single(page.ModelState[startKey]!.Errors).ErrorMessage);
            Assert.Equal("invalid-date", page.ModelState[startKey]!.AttemptedValue);
        }
        if (invalidEnd)
        {
            Assert.Equal("Indica una fecha de fin válida.", Assert.Single(page.ModelState[endKey]!.Errors).ErrorMessage);
            Assert.Equal("", page.ModelState[endKey]!.AttemptedValue);
        }
        Assert.Empty(await db.Trips.ToListAsync());
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Editor_preview_preserves_validation_failure_without_allowing_script_injection_or_revision_change()
    {
        await using var db = CreateDbContext();
        var destination = await SeedDestinationAsync(db);
        var service = CreateService(db);
        var id = await service.CreateTripAsync(new("Client", "753109", destination.Id, new(2026, 10, 1), new(2026, 10, 1), "Asia/Tokyo"));
        var state = (await service.GetEditorAsync(id))!;
        var submitted = JsonSerializer.Deserialize<TripPlanEditorPayload>(JsonSerializer.Serialize(state.Payload, JsonOptions), JsonOptions)!;
        submitted.TravelerName = "</script><script>alert('test')</script>";

        var safeJson = service.SerializeForPage(state, JsonSerializer.Serialize(submitted, JsonOptions));

        Assert.DoesNotContain("</script>", safeJson, StringComparison.OrdinalIgnoreCase);
        var recovered = JsonSerializer.Deserialize<TripPlanEditorState>(safeJson, JsonOptions)!;
        Assert.Equal(submitted.TravelerName, recovered.Payload.TravelerName);
        Assert.Equal(state.BasePlanRevision, recovered.BasePlanRevision);
        Assert.Equal("Client", (await db.Trips.SingleAsync()).TravelerName);
        Assert.Equal(service.SerializeForPage(state), service.SerializeForPage(state, "{invalid"));
    }

    [Fact]
    public async Task Create_prefills_city_segments_and_inherits_hotel_base()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);

        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente multicity",
            "864201",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 5),
            "Asia/Tokyo",
            [
                new CreateTripCitySegment("Tokyo", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), "Hotel Tokyo"),
                new CreateTripCitySegment("Kyoto", new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5))
            ]));

        var editor = (await service.GetEditorAsync(tripId))!;

        Assert.Equal(["Tokyo", "Tokyo", "Tokyo", "Kyoto", "Kyoto"], editor.Payload.Days.Select(day => day.City));
        Assert.All(editor.Payload.Days, day => Assert.Equal("Hotel Tokyo", day.HotelBase));
    }

    [Fact]
    public async Task Create_rejects_city_segments_with_date_gaps()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente multicity",
            "975301",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 5),
            "Asia/Tokyo",
            [
                new CreateTripCitySegment("Tokyo", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
                new CreateTripCitySegment("Kyoto", new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5))
            ])));

        Assert.Contains("sin huecos", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_is_not_materialized_until_publish()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var recommendation = await SeedRecommendationAsync(dbContext, destination.Id);
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Ivana & Manu",
            "190826",
            destination.Id,
            new DateOnly(2026, 11, 12),
            new DateOnly(2026, 11, 13),
            "Asia/Tokyo"));

        var editor = await service.GetEditorAsync(tripId);
        Assert.NotNull(editor);
        editor.Payload.Days[0].City = "Tokyo";
        editor.Payload.Days[1].City = "Tokyo";
        editor.Payload.Days[0].Blocks[0].CuratedDescription = "Café tranquilo cerca del hotel.";
        editor.Payload.Days[0].Blocks[0].Recommendations.Add(new TripPlanRecommendationDraft
        {
            Id = Guid.NewGuid(),
            RecommendationId = recommendation.Id
        });
        var json = JsonSerializer.Serialize(editor.Payload, JsonOptions);

        var save = await service.SaveDraftAsync(tripId, json, editor.BasePlanRevision, null);

        Assert.True(save.Success);
        var draftTrip = await dbContext.Trips.AsNoTracking().SingleAsync(item => item.Id == tripId);
        Assert.Equal(TripPublicationStatus.Draft, draftTrip.PublicationStatus);
        Assert.Null(draftTrip.AccessPinHash);
        Assert.Empty(await dbContext.TripDayPlans.ToListAsync());
        Assert.Empty(await dbContext.Reservations.ToListAsync());

        var publish = await service.PublishAsync(tripId, json, editor.BasePlanRevision, null);

        Assert.True(publish.Success, publish.Message);
        var published = await dbContext.Trips
            .AsNoTracking()
            .Include(item => item.DayPlans)
                .ThenInclude(day => day.Blocks)
            .Include(item => item.Reservations)
            .SingleAsync(item => item.Id == tripId);
        Assert.Equal(TripPublicationStatus.Published, published.PublicationStatus);
        Assert.NotNull(published.AccessPinHash);
        Assert.Equal(1, published.PlanRevision);
        Assert.Equal(2, published.DayPlans.Count);
        Assert.All(published.DayPlans, day => Assert.Equal(4, day.Blocks.Count));
        var savedRecommendation = Assert.Single(published.Reservations);
        Assert.Equal(recommendation.Id, savedRecommendation.RecommendationId);
        Assert.NotNull(savedRecommendation.TripDayBlockId);
        Assert.Equal("Café tranquilo cerca del hotel.", published.DayPlans
            .Single(day => day.DayNumber == 1)
            .Blocks.Single(block => block.PeriodKey == "morning")
            .CuratedDescription);
        Assert.Null(await dbContext.TripPlanDrafts.SingleOrDefaultAsync(item => item.TripId == tripId));
    }

    [Fact]
    public async Task Publish_rejects_stale_draft_revision()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente",
            "246802",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 1),
            "Asia/Tokyo"));
        var editor = (await service.GetEditorAsync(tripId))!;
        editor.Payload.Days[0].City = "Tokyo";
        var json = JsonSerializer.Serialize(editor.Payload, JsonOptions);
        Assert.True((await service.SaveDraftAsync(tripId, json, 0, null)).Success);

        var trip = await dbContext.Trips.SingleAsync(item => item.Id == tripId);
        trip.PlanRevision = 1;
        await dbContext.SaveChangesAsync();

        var result = await service.PublishAsync(tripId, json, 0, null);

        Assert.False(result.Success);
        Assert.Contains("cambió", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TripPublicationStatus.Draft, trip.PublicationStatus);
    }

    [Fact]
    public async Task Draft_rejects_more_than_three_recommendations_in_a_block()
    {
        await using var dbContext = CreateDbContext();
        var destination = await SeedDestinationAsync(dbContext);
        var recommendations = new List<Recommendation>();
        for (var index = 0; index < 4; index++)
        {
            recommendations.Add(await SeedRecommendationAsync(dbContext, destination.Id, $"Cafe {index}"));
        }
        var service = CreateService(dbContext);
        var tripId = await service.CreateTripAsync(new CreateTripPlanCommand(
            "Cliente",
            "135701",
            destination.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 1),
            "Asia/Tokyo"));
        var editor = (await service.GetEditorAsync(tripId))!;
        editor.Payload.Days[0].Blocks[0].Recommendations = recommendations.Select(item => new TripPlanRecommendationDraft
        {
            Id = Guid.NewGuid(),
            RecommendationId = item.Id
        }).ToList();

        var result = await service.SaveDraftAsync(
            tripId,
            JsonSerializer.Serialize(editor.Payload, JsonOptions),
            editor.BasePlanRevision,
            null);

        Assert.False(result.Success);
        Assert.Contains("hasta 3", result.Message);
    }

    private static TripPlanEditorService CreateService(TravelCompanionDbContext dbContext) =>
        new(dbContext, new PasswordHasher<Trip>(), NullLogger<TripPlanEditorService>.Instance);

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TravelCompanionDbContext(options);
    }

    private static async Task<Destination> SeedDestinationAsync(TravelCompanionDbContext dbContext)
    {
        var destination = new Destination
        {
            Id = Guid.NewGuid(),
            Name = "Japan",
            Slug = $"japan-{Guid.NewGuid():N}",
            Country = "Japan",
            TimeZoneId = "Asia/Tokyo",
            HeroImageUrl = string.Empty,
            ShortDescription = string.Empty
        };
        dbContext.Destinations.Add(destination);
        await dbContext.SaveChangesAsync();
        return destination;
    }

    private static async Task<Recommendation> SeedRecommendationAsync(
        TravelCompanionDbContext dbContext,
        Guid destinationId,
        string title = "Woodberry Coffee")
    {
        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            Title = title,
            Category = "Food",
            Neighborhood = "Tokyo, Japan",
            CitySlug = "tokyo",
            Description = "Café recomendado.",
            Tags = ["food", "cafe", "breakfast"],
            PriceLevel = "low",
            SuggestedDurationMinutes = 60,
            Latitude = 35.681m,
            Longitude = 139.767m
        };
        dbContext.Recommendations.Add(recommendation);
        await dbContext.SaveChangesAsync();
        return recommendation;
    }
}
