using System.Globalization;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class TripPreparationRecoveryTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddHours(1);

    [Fact]
    public async Task Language_defaults_cannot_hide_declarations_and_original_copies_remain_available()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var account = Session();
        cache.LocalizedEntries[("es", Key(account))] = Copy(true, Earlier,
            new PreparationCategoryState("accommodation", PreparationManualState.OutsideApp, Earlier),
            new PreparationCategoryState("travel-documents", PreparationManualState.NotNeeded, null));
        cache.LocalizedEntries[("en", Key(account))] = Copy(false, Later,
            new PreparationCategoryState("accommodation", PreparationManualState.Pending, null),
            new PreparationCategoryState("travel-documents", PreparationManualState.Pending, null));
        try
        {
            await sessions.SaveAsync(account);
            var organizer = new TripPreparationOrganizerStore(cache, sessions);
            var recovered = await organizer.GetAsync();

            Assert.Equal(PreparationManualState.OutsideApp, State(recovered, "accommodation").ManualState);
            Assert.Equal(PreparationManualState.NotNeeded, State(recovered, "travel-documents").ManualState);
            Assert.True(recovered.LegacyImported);
            Assert.Equal(4, recovered.Categories.Count);
            Assert.Equal(2, cache.LocalizedEntries.Count);
            Assert.Single(cache.Entries);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Recovery_chooses_the_last_dated_decision_for_each_category_including_explicit_pending()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var account = Session();
        cache.LocalizedEntries[("es", Key(account))] = Copy(false, Later.AddHours(1),
            new PreparationCategoryState("transport", PreparationManualState.OutsideApp, Earlier),
            new PreparationCategoryState("accommodation", PreparationManualState.OutsideApp, Later));
        cache.LocalizedEntries[("en", Key(account))] = Copy(true, Later,
            new PreparationCategoryState("transport", PreparationManualState.Pending, Later),
            new PreparationCategoryState("accommodation", PreparationManualState.NotNeeded, Earlier));
        try
        {
            await sessions.SaveAsync(account);
            var recovered = await new TripPreparationOrganizerStore(cache, sessions).GetAsync();

            Assert.Equal(PreparationManualState.Pending, State(recovered, "transport").ManualState);
            Assert.Equal(Later, State(recovered, "transport").ManualChangedAtUtc);
            Assert.Equal(PreparationManualState.OutsideApp, State(recovered, "accommodation").ManualState);
            Assert.True(recovered.LegacyImported);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Incomplete_legacy_payloads_do_not_hide_another_valid_language()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var account = Session();
        cache.LocalizedEntries[("en", Key(account))] = new OfflineCacheResult<PreparationOrganizerState>(null!, Later);
        cache.LocalizedEntries[("fr", Key(account))] = new OfflineCacheResult<PreparationOrganizerState>(new(false, null!), Later);
        cache.LocalizedEntries[("es", Key(account))] = Copy(true, Earlier,
            new PreparationCategoryState("accommodation", PreparationManualState.OutsideApp, Earlier));
        try
        {
            await sessions.SaveAsync(account);
            var recovered = await new TripPreparationOrganizerStore(cache, sessions).GetAsync();
            Assert.Equal(PreparationManualState.OutsideApp, State(recovered, "accommodation").ManualState);
            Assert.True(recovered.LegacyImported);
            Assert.Equal(3, cache.LocalizedEntries.Count);
            Assert.Single(cache.Entries);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Neutral_state_is_authoritative_and_legacy_checks_do_not_resurrect_a_pending_choice()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var account = Session();
        cache.Entries[Key(account)] = Copy(false, Later,
            new PreparationCategoryState("transport", PreparationManualState.Pending, Later));
        cache.LocalizedEntries[("es", Key(account))] = Copy(true, Earlier,
            new PreparationCategoryState("transport", PreparationManualState.OutsideApp, Earlier));
        cache.BeforeLocalizedRead = () => throw new InvalidOperationException("Neutral entries must not be merged again.");
        try
        {
            await sessions.SaveAsync(account);
            var organizer = new TripPreparationOrganizerStore(cache, sessions);
            var current = await organizer.ImportLegacyOnceAsync([new("transport", true, 1)]);

            Assert.True(current.LegacyImported);
            Assert.Equal(PreparationManualState.Pending, State(current, "transport").ManualState);
            Assert.Equal(Later, State(current, "transport").ManualChangedAtUtc);
            Assert.Single(cache.LocalizedEntries);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Explicit_pending_before_first_import_survives_checks_and_a_language_switch_after_restart()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            await sessions.SaveAsync(Session());
            CultureInfo.CurrentUICulture = new("es");
            var organizer = new TripPreparationOrganizerStore(cache, sessions);
            await organizer.SetManualStateAsync("transport", PreparationManualState.Pending);
            await organizer.SetManualStateAsync("accommodation", PreparationManualState.OutsideApp);
            var changedAt = State(await organizer.GetAsync(), "transport").ManualChangedAtUtc;
            Assert.NotNull(changedAt);

            CultureInfo.CurrentUICulture = new("en");
            var restarted = new TripPreparationOrganizerStore(cache, sessions);
            var imported = await restarted.ImportLegacyOnceAsync([new("transport", true, 1)]);
            Assert.Equal(PreparationManualState.Pending, State(imported, "transport").ManualState);
            Assert.Equal(changedAt, State(imported, "transport").ManualChangedAtUtc);
            Assert.Equal(PreparationManualState.OutsideApp, State(imported, "accommodation").ManualState);
            await restarted.SetManualStateAsync("accommodation", PreparationManualState.NotNeeded);

            CultureInfo.CurrentUICulture = new("es");
            var restored = await new TripPreparationOrganizerStore(cache, sessions).GetAsync();
            Assert.Equal(PreparationManualState.NotNeeded, State(restored, "accommodation").ManualState);
            Assert.Single(cache.Entries);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; sessions.Clear(); }
    }

    [Fact]
    public async Task Recovery_reads_only_the_active_owner_and_trip()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var first = Session();
        var second = Session();
        var anotherTrip = first with { TripId = Guid.NewGuid() };
        cache.LocalizedEntries[("es", Key(first))] = Copy(true, Earlier,
            new PreparationCategoryState("transport", PreparationManualState.OutsideApp, Earlier));
        cache.LocalizedEntries[("en", Key(second))] = Copy(true, Later,
            new PreparationCategoryState("transport", PreparationManualState.NotNeeded, Later));
        try
        {
            await sessions.SaveAsync(first);
            Assert.Equal(PreparationManualState.OutsideApp,
                State(await new TripPreparationOrganizerStore(cache, sessions).GetAsync(), "transport").ManualState);
            await sessions.SaveAsync(anotherTrip);
            Assert.Equal(PreparationManualState.Pending,
                State(await new TripPreparationOrganizerStore(cache, sessions).GetAsync(), "transport").ManualState);
            await sessions.SaveAsync(second);
            Assert.Equal(PreparationManualState.NotNeeded,
                State(await new TripPreparationOrganizerStore(cache, sessions).GetAsync(), "transport").ManualState);
            Assert.Equal(2, cache.Entries.Count);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Cancelled_recovery_preserves_localized_copies_without_writing_neutral_data()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var account = Session();
        using var cancelled = new CancellationTokenSource();
        cache.LocalizedEntries[("es", Key(account))] = Copy(true, Earlier,
            new PreparationCategoryState("transport", PreparationManualState.OutsideApp, Earlier));
        cache.BeforeLocalizedRead = () => { cancelled.Cancel(); return Task.CompletedTask; };
        try
        {
            await sessions.SaveAsync(account);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new TripPreparationOrganizerStore(cache, sessions).GetAsync(cancelled.Token));
            Assert.Empty(cache.Entries);
            Assert.Single(cache.LocalizedEntries);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Account_change_during_recovery_does_not_write_under_either_account()
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var first = Session();
        var second = Session();
        cache.LocalizedEntries[("es", Key(first))] = Copy(true, Earlier,
            new PreparationCategoryState("transport", PreparationManualState.OutsideApp, Earlier));
        cache.BeforeLocalizedRead = () => sessions.SaveAsync(second);
        try
        {
            await sessions.SaveAsync(first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new TripPreparationOrganizerStore(cache, sessions).GetAsync());
            Assert.Empty(cache.Entries);
            Assert.Single(cache.LocalizedEntries);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_removes_neutral_and_localized_copies_without_touching_another_scope(bool deleteAccount)
    {
        var sessions = new AuthSessionService();
        var cache = new OfflineCacheService();
        var first = Session();
        var anotherTrip = first with { TripId = Guid.NewGuid() };
        var second = Session();
        foreach (var account in new[] { first, anotherTrip, second })
        {
            cache.Entries[Key(account)] = Copy(true, Later);
            cache.LocalizedEntries[("es", Key(account))] = Copy(true, Earlier);
        }
        try
        {
            await sessions.SaveAsync(first);
            var documents = new TripDocumentStore(cache, sessions, new TravelCompanionApiClient(),
                new ReservationDocumentLinkStore(cache, sessions));
            if (deleteAccount) await documents.DeleteAccountAsync(first.UserId);
            else await documents.DeleteTripAsync(first.UserId, first.TripId!.Value);

            Assert.False(cache.Entries.ContainsKey(Key(first)));
            Assert.False(cache.LocalizedEntries.ContainsKey(("es", Key(first))));
            Assert.Equal(!deleteAccount, cache.Entries.ContainsKey(Key(anotherTrip)));
            Assert.Equal(!deleteAccount, cache.LocalizedEntries.ContainsKey(("es", Key(anotherTrip))));
            Assert.True(cache.Entries.ContainsKey(Key(second)));
            Assert.True(cache.LocalizedEntries.ContainsKey(("es", Key(second))));
        }
        finally { sessions.Clear(); }
    }

    private static string Key(AuthSessionDto account) => $"preparation-organizer-{account.UserId:N}-{account.TripId:N}";
    private static PreparationCategoryState State(PreparationOrganizerState state, string key) =>
        state.Categories.Single(item => item.Key == key);
    private static OfflineCacheResult<PreparationOrganizerState> Copy(bool imported, DateTimeOffset savedAt,
        params PreparationCategoryState[] categories) => new(new(imported, categories), savedAt);
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "token", Guid.NewGuid());
}
