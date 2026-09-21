using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class FreeBuilderTrialTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Free_installations_receive_isolated_trials_and_radius_limited_search()
    {
        await using var factory = new TrialApiFactory();
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        var first = await LoginAsync(client, "installation-a");
        var second = await LoginAsync(client, "installation-b");

        Assert.NotEqual(first.UserId, second.UserId);
        Assert.True(first.TrialAccess?.IsTrial);
        Assert.Equal(TrialAccessState.NotStarted, first.TrialAccess?.State);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        var response = await client.PostAsJsonAsync(
            "/api/mobile/places/search",
            new PlaceSearchRequest("ramen"),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var recommendations = await response.Content.ReadFromJsonAsync<List<RecommendationDto>>(JsonOptions);
        var result = Assert.Single(recommendations!);
        Assert.Equal(seed.InsideRecommendationId, result.Id);
        Assert.DoesNotContain(recommendations!, item => item.Id == seed.OutsideRecommendationId);
    }

    [Fact]
    public async Task First_setup_save_starts_timer_and_expired_editing_requires_upgrade()
    {
        await using var factory = new TrialApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "timer-installation");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var arrival = new DateOnly(2026, 10, 5);
        var save = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                arrival,
                arrival.AddDays(2),
                "Asia/Tokyo",
                0,
                [new BuilderTripSetupSegmentDto("Tokyo", arrival, arrival.AddDays(2))]),
            JsonOptions);
        save.EnsureSuccessStatusCode();
        var setup = await save.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions);
        Assert.NotNull(setup?.TripId);
        Assert.Equal(TrialAccessState.Editing, setup.TrialAccess?.State);
        Assert.InRange(
            setup.TrialAccess!.EditingExpiresAtUtc!.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(29),
            TimeSpan.FromMinutes(31));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var grant = await db.BuilderAccessGrants.SingleAsync(item => item.AppUserId == login.UserId && item.IsTrial);
            grant.TrialEditingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            grant.TrialDraftExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(6);
            await db.SaveChangesAsync();
        }

        var blocked = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                arrival,
                arrival.AddDays(2),
                "Asia/Tokyo",
                setup.Revision,
                [new BuilderTripSetupSegmentDto("Tokyo", arrival, arrival.AddDays(2))]),
            JsonOptions);
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
        var status = await blocked.Content.ReadFromJsonAsync<TrialAccessStatusDto>(JsonOptions);
        Assert.Equal(TrialAccessState.ReadOnly, status?.State);
        var improve = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("full day", null, "Tokyo", arrival, null, "es-ES",
                new GuidedTravelActionDto(GuidedTravelActions.FullDay),
                new GuidedPlanCriteriaDto(GuidedTravelCategories.Food, Budget: "low")), JsonOptions);
        improve.EnsureSuccessStatusCode();
        var plan = await improve.Content.ReadFromJsonAsync<TravelChatResponse>(JsonOptions);
        Assert.Equal("upgrade", plan!.MissingContext!.Field);
        Assert.Empty(plan.Cards);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Free_full_day_never_returns_a_place_outside_the_free_radius(bool transferDay)
    {
        await using var factory = new TrialApiFactory();
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "full-day-radius");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var date = new DateOnly(2026, 10, 5);
        var setup = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                date, date.AddDays(2), "Asia/Tokyo", 0,
                transferDay
                    ? [new BuilderTripSetupSegmentDto("Tokyo", date, date),
                       new BuilderTripSetupSegmentDto("Fukuoka", date, date.AddDays(2))]
                    : [new BuilderTripSetupSegmentDto("Tokyo", date, date.AddDays(2))]),
            JsonOptions);
        setup.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/ai/travel-chat",
            new TravelChatRequest(
                "full day", null, "Tokyo", date, null, "es-ES",
                new GuidedTravelActionDto(GuidedTravelActions.FullDay, "free-radius-test"),
                new GuidedPlanCriteriaDto(GuidedTravelCategories.Food, Budget: "low")),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<TravelChatResponse>(JsonOptions);

        Assert.NotNull(plan);
        Assert.NotEmpty(plan.Cards);
        Assert.All(plan.Cards, card => Assert.Equal(seed.InsideRecommendationId.ToString(), card.RecommendationId));
        Assert.DoesNotContain(plan.Cards, card => card.RecommendationId == seed.OutsideRecommendationId.ToString());
    }

    [Fact]
    public async Task Retired_planning_features_are_not_available_to_older_clients()
    {
        await using var factory = new TrialApiFactory();
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "proposal-radius");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var date = new DateOnly(2026, 10, 5);
        var setupResponse = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                date, date.AddDays(2), "Asia/Tokyo", 0,
                [new BuilderTripSetupSegmentDto("Tokyo", date, date.AddDays(2))]),
            JsonOptions);
        setupResponse.EnsureSuccessStatusCode();
        var setup = await setupResponse.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions);

        var response = await client.PostAsJsonAsync(
            "/api/mobile/proposals",
            new DayProposalRequestDto(
                date,
                DayPlanningGoal.Balance,
                setup!.Revision,
                new TimeOnly(9, 0),
                new TimeOnly(21, 0),
                "free-proposal-radius"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var redeem = await client.PostAsJsonAsync("/api/mobile/pass/redeem", new RedeemTravelPassRequest("4321"), JsonOptions);
        redeem.EnsureSuccessStatusCode();
        var paid = await redeem.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paid!.Token);
        var routes = await client.GetAsync("/api/mobile/thematic-routes");
        Assert.Equal(HttpStatusCode.Gone, routes.StatusCode);

    }

    [Fact]
    public async Task Long_free_trip_is_allowed_but_day_four_planning_requires_a_pass()
    {
        await using var factory = new TrialApiFactory();
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "long-trip");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var date = new DateOnly(2026, 10, 5);
        var setup = await client.PutAsJsonAsync("/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(date, date.AddDays(9), "Asia/Tokyo", 0,
                [new BuilderTripSetupSegmentDto("Tokyo", date, date.AddDays(9))]), JsonOptions);
        setup.EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/ai/travel-chat",
            new TravelChatRequest("full day", null, "Tokyo", date.AddDays(3), null, "es-ES",
                new GuidedTravelActionDto(GuidedTravelActions.FullDay),
                new GuidedPlanCriteriaDto(GuidedTravelCategories.Food, Budget: "low")), JsonOptions);
        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<TravelChatResponse>(JsonOptions);
        Assert.Equal("upgrade", plan!.MissingContext!.Field);
        Assert.Empty(plan.Cards);
        var save = await client.PostAsJsonAsync("/api/ai/save-itinerary-item",
            new SaveItineraryItemRequest(seed.InsideRecommendationId, date.AddDays(3), new TimeOnly(9, 0), null), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        Assert.Empty(await db.AssistantUsageLeases.ToListAsync());
        Assert.Empty(await db.Reservations.Where(r => r.RecommendationId == seed.InsideRecommendationId).ToListAsync());
    }

    [Fact]
    public async Task Paid_pin_promotes_the_same_draft_to_builder_access()
    {
        await using var factory = new TrialApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "buyer-installation");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var arrival = new DateOnly(2026, 11, 1);
        var setupResponse = await client.PutAsJsonAsync(
            "/api/mobile/builder/setup",
            new SaveBuilderTripSetupRequest(
                arrival,
                arrival.AddDays(4),
                "Asia/Tokyo",
                0,
                [new BuilderTripSetupSegmentDto("Tokyo", arrival, arrival.AddDays(4))]),
            JsonOptions);
        setupResponse.EnsureSuccessStatusCode();
        var setup = await setupResponse.Content.ReadFromJsonAsync<BuilderTripSetupDto>(JsonOptions);

        var redeem = await client.PostAsJsonAsync(
            "/api/mobile/pass/redeem",
            new RedeemTravelPassRequest("4321"),
            JsonOptions);
        redeem.EnsureSuccessStatusCode();
        var paid = await redeem.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions);
        Assert.Equal(SessionAccessMode.Builder, paid?.AccessMode);
        Assert.Equal(setup?.TripId, paid?.TripId);
        Assert.False(paid?.TrialAccess?.IsTrial);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var grants = await db.BuilderAccessGrants.Where(item => item.AppUserId == login.UserId).ToListAsync();
        Assert.Contains(grants, item => item.IsTrial && item.ConvertedAtUtc.HasValue && item.Status == BuilderAccessStatus.Revoked);
        Assert.Contains(grants, item => !item.IsTrial && item.TripId == setup!.TripId && item.Status == BuilderAccessStatus.Active);
    }

    private static async Task<AuthSessionDto> LoginAsync(HttpClient client, string instanceId)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync(
            "/api/auth/pin-login",
            new PinLoginRequestDto("0000", instanceId),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonOptions))!;
    }

    private sealed class TrialApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"free-builder-trial-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }

        public async Task<SeedResult> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            if (await db.Destinations.AnyAsync())
            {
                var existing = await db.Recommendations.OrderBy(item => item.Title).ToListAsync();
                return new(existing[0].Id, existing[1].Id);
            }

            var destination = new Destination
            {
                Id = Guid.NewGuid(),
                Name = "Japón",
                Slug = "japon",
                Country = "Japan",
                TimeZoneId = "Asia/Tokyo",
                HeroImageUrl = string.Empty,
                ShortDescription = string.Empty
            };
            var inside = Recommendation(destination.Id, "Inside ramen", 35.6815m, 139.7671m);
            var outside = Recommendation(destination.Id, "Outside ramen", 35.7500m, 139.7671m);
            var paidAccount = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "unused-pass@example.test",
                DisplayName = "Unused pass",
                PasswordHash = string.Empty
            };
            var paidGrant = new BuilderAccessGrant
            {
                Id = Guid.NewGuid(),
                AppUserId = paidAccount.Id,
                DestinationId = destination.Id,
                PinHash = string.Empty,
                OrderReference = "test-order"
            };
            paidGrant.PinHash = new PasswordHasher<BuilderAccessGrant>().HashPassword(paidGrant, "4321");
            db.AddRange(
                destination,
                new FreeMapCity
                {
                    Id = Guid.NewGuid(),
                    DestinationId = destination.Id,
                    CitySlug = "tokyo",
                    DisplayName = "Tokyo",
                    CenterLatitude = 35.681236m,
                    CenterLongitude = 139.767125m,
                    FreeRadiusKm = 2m,
                    CoverageRadiusKm = 20m,
                    IsEnabled = true,
                    SortOrder = 1
                },
                inside,
                outside,
                paidAccount,
                paidGrant);
            await db.SaveChangesAsync();
            return new(inside.Id, outside.Id);
        }

        private static Recommendation Recommendation(Guid destinationId, string title, decimal latitude, decimal longitude) => new()
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId,
            ExternalId = title.Replace(' ', '-').ToLowerInvariant(),
            Title = title,
            Category = "Food",
            Neighborhood = "Tokyo",
            Description = "ramen local",
            Tags = ["ramen"],
            PriceLevel = "low",
            Latitude = latitude,
            Longitude = longitude,
            SuggestedDurationMinutes = 60,
            AccessLevel = ContentAccessLevel.Free
        };

        public sealed record SeedResult(Guid InsideRecommendationId, Guid OutsideRecommendationId);
    }
}
