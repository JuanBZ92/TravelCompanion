using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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

public sealed class ItineraryRouteEndpointTests
{
    [Fact]
    public async Task Legacy_json_without_new_fields_keeps_ten_minute_hotel_default()
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        using var body = new StringContent("""{"mode":"WALK"}""", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(state.Path, body); response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal(10, result!.ArrivalMarginMinutes); Assert.Equal("Hotel", result.OriginKind);
        Assert.Equal(state.Start.AddMinutes(-10), app.Routes.Arrival);
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(30, false)]
    public async Task Previous_activity_conflict_is_reported_without_guaranteeing_the_connection(int margin, bool overlaps)
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        var request = new ItineraryRouteRequest("WALK") { OriginKind = "PreviousActivity", PreviousActivityId = state.PreviousId, ArrivalMarginMinutes = margin };
        var response = await client.PostAsJsonAsync(state.Path, request); response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal(overlaps, result!.PreviousActivityOverlapsDeparture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Hotel")]
    public async Task Existing_and_explicit_hotel_requests_use_verified_hotel_and_margin(string? kind)
    {
        await using var app = new Factory(); var state = await app.Seed();
        using var client = app.Client(state.Token);
        var response = await client.PostAsJsonAsync(state.Path, new ItineraryRouteRequest("WALK") { OriginKind = kind, ArrivalMarginMinutes = 30 });
        response.EnsureSuccessStatusCode(); var result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal("Available", result!.Status); Assert.Equal(30, result.ArrivalMarginMinutes);
        Assert.Equal("hotel-place", app.Routes.Origin!.PlaceId); Assert.Equal("target-place", app.Routes.Destination!.PlaceId);
        Assert.Equal(state.Start.AddMinutes(-30), app.Routes.Arrival);
    }

    [Fact]
    public async Task Previous_activity_and_provider_failure_keep_safe_destination_and_origin()
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        var request = new ItineraryRouteRequest("TRANSIT") { OriginKind = "PreviousActivity", PreviousActivityId = state.PreviousId, ArrivalMarginMinutes = 15 };
        var response = await client.PostAsJsonAsync(state.Path, request); response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal("PreviousActivity", result!.OriginKind); Assert.Equal("previous-place", app.Routes.Origin!.PlaceId);
        app.Routes.Fail = true;
        response = await client.PostAsJsonAsync(state.Path, request); response.EnsureSuccessStatusCode();
        result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal("Unavailable", result!.Status); Assert.Equal("Previous", result.Origin);
        using var scope = app.Services.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>().Reservations.FindAsync(state.TargetId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task Invalid_margin_is_rejected_without_provider_call(int margin)
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        var response = await client.PostAsJsonAsync(state.Path, new ItineraryRouteRequest("WALK") { ArrivalMarginMinutes = margin });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, app.Routes.Calls);
    }

    [Fact]
    public async Task Previous_activity_across_date_line_uses_same_utc_window_as_mobile()
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var destination = await db.Reservations.FindAsync(state.TargetId);
            var previous = await db.Reservations.FindAsync(state.PreviousId);
            destination!.TimeZoneId = "Etc/GMT+12"; destination.StartsAt = new(23, 0);
            previous!.Date = destination.Date.AddDays(2); previous.TimeZoneId = "Pacific/Kiritimati";
            previous.StartsAt = new(0, 0); previous.DurationMinutes = 30;
            await db.SaveChangesAsync();
        }
        var request = new ItineraryRouteRequest("WALK") { OriginKind = "PreviousActivity", PreviousActivityId = state.PreviousId };
        var response = await client.PostAsJsonAsync(state.Path, request); response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ItineraryRouteDto>();
        Assert.Equal("Available", result!.Status); Assert.Equal("previous-place", app.Routes.Origin!.PlaceId);
    }

    [Fact]
    public async Task Foreign_previous_and_destination_are_rejected_before_provider_call()
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        var request = new ItineraryRouteRequest("WALK") { OriginKind = "PreviousActivity", PreviousActivityId = state.ForeignId };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(state.Path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/mobile/itinerary/{state.ForeignId}/route", new ItineraryRouteRequest("WALK"))).StatusCode);
        Assert.Equal(0, app.Routes.Calls);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task Free_revoked_and_expired_access_never_call_provider(string access)
    {
        await using var app = new Factory(); var state = await app.Seed(access); using var client = app.Client(state.Token);
        var response = await client.PostAsJsonAsync(state.Path, new ItineraryRouteRequest("WALK"));
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, response.StatusCode.ToString());
        Assert.Equal(0, app.Routes.Calls);
    }

    [Fact]
    public async Task Current_location_requires_a_complete_coordinate_pair()
    {
        await using var app = new Factory(); var state = await app.Seed(); using var client = app.Client(state.Token);
        var invalid = new ItineraryRouteRequest("WALK") { OriginKind = "CurrentLocation" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(state.Path, invalid)).StatusCode);
        var valid = new ItineraryRouteRequest("DRIVE", 35m, 139m) { OriginKind = "CurrentLocation" };
        (await client.PostAsJsonAsync(state.Path, valid)).EnsureSuccessStatusCode();
        Assert.Equal(35m, app.Routes.Origin!.Latitude); Assert.Null(app.Routes.Origin.PlaceId);
    }

    private sealed record Seeded(string Token, Guid TargetId, Guid PreviousId, Guid ForeignId, DateTimeOffset Start)
    {
        public string Path => $"/api/mobile/itinerary/{TargetId}/route";
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string dbName = "r13-" + Guid.NewGuid().ToString("N");
        public FakeRoutes Routes { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureServices(services => {
                services.RemoveAll<DbContextOptions<TravelCompanionDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<TravelCompanionDbContext>>();
                services.AddDbContext<TravelCompanionDbContext>(options => options.UseInMemoryDatabase(dbName));
                services.RemoveAll<IGoogleRoutesService>(); services.AddSingleton<IGoogleRoutesService>(Routes);
            });
        }
        public HttpClient Client(string token) { var client = CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
        public async Task<Seeded> Seed(string access = "paid")
        {
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
            var world = await DayPlanTestWorld.SeedAsync(db, perCity: 0);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)); world.Trip.StartsOn = date; world.Trip.EndsOn = date.AddDays(2);
            var target = Reservation(world.Trip.Id, date, 12, "Target", "target-place");
            var previous = Reservation(world.Trip.Id, date, 9, "Previous", "previous-place");
            var foreignUser = new AppUser { Id = Guid.NewGuid(), Email = "foreign@example.test", DisplayName = "Other" };
            var foreignTrip = new Trip { Id = Guid.NewGuid(), AppUserId = foreignUser.Id, DestinationId = world.Trip.DestinationId, TravelerName = "Other", StartsOn = date, EndsOn = date.AddDays(2) };
            var foreign = Reservation(foreignTrip.Id, date, 10, "Foreign", "foreign-place");
            db.AddRange(target, previous, foreignUser, foreignTrip, foreign);
            db.TripDayPlans.Add(new() { Id = Guid.NewGuid(), TripId = world.Trip.Id, Date = date, HotelBase = "Hotel", BaseProviderPlaceId = "hotel-place", City = "Tokyo" });
            if (access == "revoked") world.Grant.RevokedAtUtc = DateTimeOffset.UtcNow;
            if (access == "expired") world.Grant.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            var sessions = scope.ServiceProvider.GetRequiredService<UserSessionService>();
            var (_, token) = await sessions.CreateSessionAsync(world.User, tripId: world.Trip.Id,
                accessMode: access == "free" ? SessionAccessMode.FreeMapPreview : SessionAccessMode.Builder);
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new(12, 0)), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")));
            return new(token, target.Id, previous.Id, foreign.Id, start);
        }
        private static Reservation Reservation(Guid tripId, DateOnly date, int hour, string title, string place) => new()
        {
            Id = Guid.NewGuid(), TripId = tripId, Date = date, StartsAt = new(hour, 0), DurationMinutes = 60,
            Title = title, City = "Tokyo", LocationName = title, Address = title + " address", ConfirmationCode = "", Notes = "", ProviderPlaceId = place
        };
    }
    private sealed class FakeRoutes : IGoogleRoutesService
    {
        public RouteWaypoint? Origin; public RouteWaypoint? Destination; public DateTimeOffset Arrival; public bool Fail; public int Calls;
        public Task<RouteEstimate?> EstimateAsync(RouteWaypoint origin, RouteWaypoint destination, string mode, DateTimeOffset arrival, CancellationToken ct)
        {
            Calls++; Origin = origin; Destination = destination; Arrival = arrival;
            return Task.FromResult<RouteEstimate?>(Fail ? null : new(20, arrival.AddMinutes(-20)));
        }
    }
}
