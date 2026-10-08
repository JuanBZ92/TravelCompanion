using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class PostgresExpressConversationTests
{
    [PostgresItineraryIdempotencyTests.PostgresFact]
    public async Task Complete_express_history_survives_reload_without_overflowing_the_legacy_column()
    {
        await using var database = new PerformanceDatabase();
        await database.InitializeAsync();
        await using var db = database.Open();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "express-history@example.test", DisplayName = "Synthetic traveler" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var history = Enumerable.Range(0, 104).Select(_ => Guid.NewGuid().ToString()).ToList();
        var date = new DateOnly(2026, 10, 8);
        var state = new TravelAssistantConversationState
        {
            LastDate = date, LastCity = "Tokyo", LastRecommendationIds = history,
            GuidedCriteria = new("culture")
            {
                WindowStartsAtLocal = date.ToDateTime(new(10, 0)),
                WindowEndsAtLocal = date.ToDateTime(new(11, 0)), WindowTimeZoneId = "Asia/Tokyo",
                ExcludedRecommendationIds = history.Select(Guid.Parse).ToArray()
            }
        };
        var service = new TravelAssistantConversationStateService(db, NullLogger<TravelAssistantConversationStateService>.Instance);
        var id = Guid.NewGuid().ToString("N");
        await service.SavePlanningStateAsync(null, id, user.Id, state, default);
        await using var verify = database.Open();
        var persisted = await verify.TravelChatConversations.SingleAsync(item => item.Id == id);
        Assert.InRange(persisted.LastRecommendationIds!.Length, 1, 512);
        Assert.NotNull(persisted.StateJson);
        var reloaded = service.ReadState(persisted);
        Assert.Equal(history, reloaded.LastRecommendationIds);
        Assert.Equal(history.Select(Guid.Parse), reloaded.GuidedCriteria!.ExcludedRecommendationIds!);
        Assert.Equal(date.ToDateTime(new(10, 0)), reloaded.GuidedCriteria.WindowStartsAtLocal);

        // Old records with no JSON still recover the legacy projection.
        persisted.StateJson = null;
        Assert.Equal(history.TakeLast(13), service.ReadState(persisted).LastRecommendationIds);
    }
}
