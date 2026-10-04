using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record DayPlannerDraft(DayPlanOptionsDto? Options, DayPlanRequest? Request,
    DayPlanResponse? Proposal, IReadOnlyList<Guid> SelectedStopIds, DayPlanApplyRequest? PendingApplication = null,
    IReadOnlyList<Guid>? SavedStopIds = null, int? AppliedRevision = null,
    DateOnly? SelectedDate = null, int DayCount = 1, DayPlanPreferencesDto? Preferences = null);

// Scope keys and the existing encrypted atomic writer keep previews private/offline.
public sealed class DayPlannerStore(OfflineCacheService cache, AuthSessionService sessions)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<Guid> deletedUsers = [];
    private readonly HashSet<(Guid, Guid)> deletedTrips = [];
    private static string Key(Guid user, Guid trip) => $"personal-planner-{user}-{trip}";
    private void Check(Guid user, Guid trip)
    {
        if (!sessions.HasSession || sessions.CurrentUserId != user || sessions.CurrentTripId != trip
            || deletedUsers.Contains(user) || deletedTrips.Contains((user, trip))) throw new OperationCanceledException();
    }
    public async Task<DayPlannerDraft?> ReadAsync(Guid user, Guid trip, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Check(user, trip);
            var value = (await cache.GetAsync<DayPlannerDraft>(Key(user, trip), cancellationToken: ct))?.Value;
            Check(user, trip);
            return value;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(Guid user, Guid trip, DayPlannerDraft draft, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { Check(user, trip); await cache.SaveAsync(Key(user, trip), draft, ct); Check(user, trip); }
        finally { gate.Release(); }
    }
    public async Task DeleteAccountAsync(Guid user)
    {
        await gate.WaitAsync();
        try { deletedUsers.Add(user); await cache.DeleteByPrefixAsync($"personal-planner-{user}-"); }
        finally { gate.Release(); }
    }
    public async Task DeleteTripAsync(Guid user, Guid trip)
    {
        await gate.WaitAsync();
        try { deletedTrips.Add((user, trip)); await cache.DeleteByPrefixAsync(Key(user, trip)); }
        finally { gate.Release(); }
    }
    // A proposal is authorized for its original account. Linking drops its preview;
    // confirmed itinerary items transfer through the backend's existing account link.
    public async Task DiscardLinkedPreviewAsync(AuthSessionDto verified)
    {
        if (verified.LinkedFromUserId is not { } source || verified.UserId == source
            || sessions.CurrentUserId != source) return;
        await gate.WaitAsync();
        try
        {
            deletedUsers.Add(source);
            await cache.DeleteByPrefixAsync($"personal-planner-{source}-");
        }
        finally { gate.Release(); }
    }
}
