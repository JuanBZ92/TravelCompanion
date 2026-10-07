using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed partial class TravelChatEndpointTests
{
    private static async Task<(string Token, TravelChatRequest Request)> SeedFreeTimeEndpointAsync(TravelCompanionApiFactory factory)
    {
        await factory.SeedPlanningUserAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        var user = await db.AppUsers.SingleAsync();
        var trip = await db.Trips.SingleAsync();
        trip.StartsOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        trip.EndsOn = trip.StartsOn.AddDays(4);
        trip.TimeZoneId = "Asia/Tokyo";
        db.BuilderAccessGrants.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, DestinationId = trip.DestinationId,
            TripId = trip.Id, IsTrial = false, Status = BuilderAccessStatus.Active,
            PurchasedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var sessions = scope.ServiceProvider.GetRequiredService<UserSessionService>();
        var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: SessionAccessMode.Builder);
        return (token, new("What can I do in this free time?", null, "Tokyo", trip.StartsOn, null, "en-US",
            new(GuidedTravelActions.Recommend), new(GuidedTravelCategories.Food)
            {
                WindowStartsAtLocal = trip.StartsOn.ToDateTime(new(10, 0)),
                WindowEndsAtLocal = trip.StartsOn.ToDateTime(new(12, 0)),
                WindowTimeZoneId = trip.TimeZoneId
            }, Guid.NewGuid()));
    }

    [Fact]
    public async Task Free_time_exact_retry_consumes_a_single_existing_assistant_quota()
    {
        await using var factory = new TravelCompanionApiFactory();
        var (token, request) = await SeedFreeTimeEndpointAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var firstHttp = await client.PostAsJsonAsync("/api/ai/travel-chat", request);
        firstHttp.EnsureSuccessStatusCode();
        var first = await firstHttp.Content.ReadFromJsonAsync<TravelChatResponse>();
        Assert.NotEmpty(first!.Cards);
        var retryHttp = await client.PostAsJsonAsync("/api/ai/travel-chat", request);
        retryHttp.EnsureSuccessStatusCode();
        var retry = await retryHttp.Content.ReadFromJsonAsync<TravelChatResponse>();
        Assert.Equal(first.Cards.Select(item => item.RecommendationId), retry!.Cards.Select(item => item.RecommendationId));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        Assert.Equal(1, (await db.AssistantDailyUsages.SingleAsync()).SuccessfulRequests);
        Assert.Single(await db.AssistantUsageLeases.Where(item => item.CompletedAtUtc != null).ToListAsync());
    }

    [Fact]
    public async Task Free_time_invalid_interval_does_not_consume_assistant_quota()
    {
        await using var factory = new TravelCompanionApiFactory();
        var (token, request) = await SeedFreeTimeEndpointAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var http = await client.PostAsJsonAsync("/api/ai/travel-chat", request with
        {
            Criteria = request.Criteria! with { WindowEndsAtLocal = null }
        });
        http.EnsureSuccessStatusCode();
        var body = await http.Content.ReadFromJsonAsync<TravelChatResponse>();
        Assert.Equal("time_window", body!.MissingContext?.Field);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>();
        Assert.Empty(await db.AssistantDailyUsages.ToListAsync());
        Assert.Empty(await db.AssistantUsageLeases.Where(item => item.CompletedAtUtc != null).ToListAsync());
    }
}
