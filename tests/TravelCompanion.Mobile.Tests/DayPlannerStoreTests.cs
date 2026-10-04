using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class DayPlannerStoreTests
{
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "planner@example.test", "Planner", false,
        "token", Guid.NewGuid(), AccessMode: SessionAccessMode.FreeMapPreview, ExperienceMode: ExperienceMode.FreePreview);

    private static DayPlannerDraft Draft(Guid trip)
    {
        var operation = Guid.NewGuid();
        var stop = Guid.NewGuid();
        var recommendation = Guid.NewGuid();
        var card = new TravelCardDto("recommendation", "Museum", "Tokyo", null, null, null, null, null, null,
            [], [], recommendation.ToString(), null) { IsPeriodOnly = true, PeriodKey = "morning" };
        var proposal = new DayPlanResponse(operation, trip, 5,
            [new(new(2026, 10, 20), ["Tokyo"], [new(stop, recommendation, "morning", card)], [])], "Ideas");
        var request = new DayPlanRequest(trip, 5, new(2026, 10, 20), 3, operation, new("efficient", "medium", ["Culture"]));
        return new(null, request, proposal, [stop], new(operation, trip, 5, Guid.NewGuid(), [stop]),
            SelectedDate: request.StartDate, DayCount: 3, Preferences: request.Preferences);
    }

    [Fact]
    public async Task Restart_restores_proposal_selection_preferences_and_pending_mutation_offline()
    {
        var sessions = new AuthSessionService();
        var session = Session();
        var cache = new OfflineCacheService();
        try
        {
            await sessions.SaveAsync(session);
            var draft = Draft(session.TripId!.Value);
            await new DayPlannerStore(cache, sessions).SaveAsync(session.UserId, session.TripId.Value, draft);
            var restarted = new DayPlannerStore(cache, sessions);
            var restored = await restarted.ReadAsync(session.UserId, session.TripId.Value);
            Assert.Equal(draft.Proposal!.OperationId, restored!.Proposal!.OperationId);
            Assert.Equal(draft.SelectedStopIds, restored.SelectedStopIds);
            Assert.Equal(draft.PendingApplication!.MutationId, restored.PendingApplication!.MutationId);
            Assert.Equal(3, restored.DayCount);
            Assert.Equal("efficient", restored.Preferences!.TravelPace);
            Assert.Equal(draft.SelectedDate, restored.SelectedDate);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Cache_is_isolated_between_users_and_trips_and_rejects_wrong_scope()
    {
        var sessions = new AuthSessionService();
        var original = Session();
        var cache = new OfflineCacheService();
        var store = new DayPlannerStore(cache, sessions);
        try
        {
            await sessions.SaveAsync(original);
            await store.SaveAsync(original.UserId, original.TripId!.Value, Draft(original.TripId.Value));
            var other = Session();
            await sessions.SaveAsync(other);
            Assert.Null(await store.ReadAsync(other.UserId, other.TripId!.Value));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync(original.UserId, original.TripId.Value));
            await sessions.SaveAsync(original with { TripId = Guid.NewGuid() });
            Assert.Null(await store.ReadAsync(original.UserId, sessions.CurrentTripId!.Value));
            await sessions.SaveAsync(original);
            Assert.NotNull(await store.ReadAsync(original.UserId, original.TripId.Value));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Account_change_while_reading_discards_the_result()
    {
        var sessions = new AuthSessionService();
        var original = Session();
        var cache = new OfflineCacheService();
        var store = new DayPlannerStore(cache, sessions);
        try
        {
            await sessions.SaveAsync(original);
            await store.SaveAsync(original.UserId, original.TripId!.Value, Draft(original.TripId.Value));
            cache.BeforeRead = () => sessions.SaveAsync(Session());
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync(original.UserId, original.TripId.Value));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_removes_preview_and_prevents_late_writes(bool account)
    {
        var sessions = new AuthSessionService();
        var session = Session();
        var cache = new OfflineCacheService();
        var store = new DayPlannerStore(cache, sessions);
        try
        {
            await sessions.SaveAsync(session);
            var draft = Draft(session.TripId!.Value);
            await store.SaveAsync(session.UserId, session.TripId.Value, draft);
            if (account) await store.DeleteAccountAsync(session.UserId);
            else await store.DeleteTripAsync(session.UserId, session.TripId.Value);
            Assert.Empty(cache.Entries);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(session.UserId, session.TripId.Value, draft));
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_account_link_discards_old_proposal_and_blocks_source_late_writes(bool targetTripChanged)
    {
        var sessions = new AuthSessionService();
        var original = Session();
        var cache = new OfflineCacheService();
        var store = new DayPlannerStore(cache, sessions);
        try
        {
            await sessions.SaveAsync(original);
            var draft = Draft(original.TripId!.Value);
            await store.SaveAsync(original.UserId, original.TripId.Value, draft);
            var linked = original with
            {
                UserId = Guid.NewGuid(), LinkedFromUserId = original.UserId,
                TripId = targetTripChanged ? Guid.NewGuid() : original.TripId
            };
            await store.DiscardLinkedPreviewAsync(linked);
            Assert.Empty(cache.Entries);
            await sessions.SaveAsync(linked);
            Assert.Null(await store.ReadAsync(linked.UserId, linked.TripId!.Value));
            await sessions.SaveAsync(original);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(original.UserId, original.TripId.Value, draft));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Older_cached_draft_without_new_settings_remains_readable()
    {
        var sessions = new AuthSessionService();
        var session = Session();
        var cache = new OfflineCacheService();
        try
        {
            await sessions.SaveAsync(session);
            var older = new DayPlannerDraft(null, null, null, []);
            await new DayPlannerStore(cache, sessions).SaveAsync(session.UserId, session.TripId!.Value, older);
            var restored = await new DayPlannerStore(cache, sessions).ReadAsync(session.UserId, session.TripId.Value);
            Assert.Equal(1, restored!.DayCount);
            Assert.Null(restored.SelectedDate);
            Assert.Null(restored.Preferences);
        }
        finally { sessions.Clear(); }
    }
}
