using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class AuthSessionAtomicTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Session_persistence_that_finishes_after_logout_never_resurrects_profile_or_token(bool clear)
    {
        var session = new AuthSessionService();
        var initial = Session("initial-token"); var next = Session("delayed-token");
        var entered = Signal(); var release = Signal(); string? stagedKey = null;
        try
        {
            await session.SaveAsync(initial);
            SecureStorage.Default.BeforeSet = async (key, value) => { if (value == next.Token) { stagedKey = key; entered.TrySetResult(); await release.Task; } };
            var write = session.SaveAsync(next);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(initial.UserId, session.CurrentUserId);
            Assert.Equal(initial.Token, await session.GetTokenAsync());
            if (clear) session.Clear(); else session.BeginLogout();
            release.TrySetResult(); await write;
            Assert.False(session.HasSession);
            Assert.Null(await session.GetTokenAsync());
            Assert.Null(await SecureStorage.Default.GetAsync(stagedKey!));
            if (clear) Assert.Null(session.CurrentUserId);
            else Assert.Equal(initial.UserId, session.CurrentUserId);
        }
        finally { release.TrySetResult(); SecureStorage.Default.BeforeSet = null; session.Clear(); }
    }

    [Fact]
    public async Task Conditional_save_obsolete_before_start_never_writes_or_changes_the_active_session()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var current = Session("current-token");
        var writes = 0;
        try
        {
            await session.SaveAsync(initial); var oldVersion = session.ContextVersion;
            await session.SaveAsync(current);
            SecureStorage.Default.BeforeSet = (_, _) => { writes++; return Task.CompletedTask; };
            Assert.False(await session.SaveIfCurrentAsync(initial, oldVersion));
            Assert.Equal(0, writes);
            Assert.Equal(current.UserId, session.CurrentUserId);
            Assert.Equal(current.Token, await session.GetTokenAsync());
        }
        finally { SecureStorage.Default.BeforeSet = null; session.Clear(); }
    }

    [Fact]
    public async Task Conditional_save_invalidated_during_token_write_retains_the_active_profile_and_previous_token()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var result = initial with { Token = "new-token" };
        var entered = Signal(); var release = Signal(); string? stagedKey = null;
        try
        {
            await session.SaveAsync(initial); var expected = session.ContextVersion;
            SecureStorage.Default.BeforeSet = async (key, value) => { if (value == result.Token) { stagedKey = key; entered.TrySetResult(); await release.Task; } };
            var write = session.SaveIfCurrentAsync(result, expected);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var newTrip = Guid.NewGuid(); session.MarkTripConfigured(newTrip);
            release.TrySetResult(); Assert.False(await write);
            Assert.Equal(initial.UserId, session.CurrentUserId);
            Assert.Equal(newTrip, session.CurrentTripId);
            Assert.Equal(initial.Token, await session.GetTokenAsync());
            Assert.Null(await SecureStorage.Default.GetAsync(stagedKey!));
        }
        finally { release.TrySetResult(); SecureStorage.Default.BeforeSet = null; session.Clear(); }
    }

    [Fact]
    public async Task Failed_secure_write_keeps_previous_profile_token_and_pointer_without_notifying_new_session()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var next = Session("failed-token");
        var events = 0; string? stagedKey = null;
        try
        {
            await session.SaveAsync(initial); var initialVersion = session.ContextVersion;
            var pointer = Preferences.Default.Get("auth_token_storage_key", string.Empty);
            session.StateChanged += (_, _) => events++;
            SecureStorage.Default.BeforeSet = (key, _) => { stagedKey = key; throw new IOException("synthetic storage failure"); };
            await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(next));
            Assert.Equal(initial.UserId, session.CurrentUserId);
            Assert.Equal(initial.TripId, session.CurrentTripId);
            Assert.Equal(initial.Token, await session.GetTokenAsync());
            Assert.Equal(initialVersion, session.ContextVersion);
            Assert.Equal(pointer, Preferences.Default.Get("auth_token_storage_key", string.Empty));
            Assert.Equal(0, events);
            Assert.Null(await SecureStorage.Default.GetAsync(stagedKey!));
        }
        finally { SecureStorage.Default.BeforeSet = null; session.Clear(); }
    }

    [Fact]
    public async Task Concurrent_normal_saves_are_serialized_and_publish_each_profile_with_its_own_token()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var first = Session("first-token"); var last = Session("last-token");
        var entered = Signal(); var release = Signal(); var writes = 0;
        try
        {
            await session.SaveAsync(initial);
            SecureStorage.Default.BeforeSet = async (_, value) =>
            {
                writes++;
                if (value == first.Token) { entered.TrySetResult(); await release.Task; }
                else if (value == last.Token)
                {
                    Assert.Equal(first.UserId, session.CurrentUserId);
                    Assert.Equal(first.Token, await session.GetTokenAsync());
                }
            };
            var firstWrite = session.SaveAsync(first);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var lastWrite = session.SaveAsync(last);
            Assert.Equal(1, writes);
            Assert.Equal(initial.UserId, session.CurrentUserId);
            Assert.Equal(initial.Token, await session.GetTokenAsync());
            release.TrySetResult(); await Task.WhenAll(firstWrite, lastWrite);
            Assert.Equal(2, writes);
            Assert.Equal(last.UserId, session.CurrentUserId);
            Assert.Equal(last.TripId, session.CurrentTripId);
            Assert.Equal(last.Token, await session.GetTokenAsync());
            Assert.Equal(last.Token, await new AuthSessionService().GetTokenAsync());
        }
        finally { release.TrySetResult(); SecureStorage.Default.BeforeSet = null; session.Clear(); }
    }

    [Fact]
    public async Task Failure_removing_previous_token_never_deletes_newly_published_token_or_blocks_later_saves()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var next = Session("next-token");
        string? previousKey = null;
        try
        {
            await session.SaveAsync(initial);
            previousKey = Preferences.Default.Get("auth_token_storage_key", string.Empty);
            SecureStorage.Default.BeforeRemove = key => { if (key == previousKey) throw new IOException("synthetic cleanup failure"); };
            await session.SaveAsync(next);
            Assert.Equal(next.UserId, session.CurrentUserId);
            Assert.Equal(next.Token, await session.GetTokenAsync());
            var last = Session("last-token"); await session.SaveAsync(last).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(last.UserId, session.CurrentUserId);
            Assert.Equal(last.Token, await session.GetTokenAsync());
        }
        finally
        {
            SecureStorage.Default.BeforeRemove = null;
            if (previousKey is not null) SecureStorage.Default.Remove(previousKey);
            session.Clear();
        }
    }

    [Fact]
    public async Task Token_read_does_not_hold_the_write_gate_and_discards_a_result_from_a_previous_pointer()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token"); var next = Session("next-token");
        var entered = Signal(); var release = Signal();
        try
        {
            await session.SaveAsync(initial);
            SecureStorage.Default.BeforeGet = async key => { if (key.StartsWith("auth_token", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; } };
            var read = session.GetTokenAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.SaveAsync(next).WaitAsync(TimeSpan.FromSeconds(5));
            release.TrySetResult(); Assert.Null(await read);
            SecureStorage.Default.BeforeGet = null;
            Assert.Equal(next.Token, await session.GetTokenAsync());
        }
        finally { release.TrySetResult(); SecureStorage.Default.BeforeGet = null; session.Clear(); }
    }

    [Fact]
    public async Task Legacy_token_can_be_read_and_is_removed_only_after_successful_staged_publication()
    {
        var session = new AuthSessionService(); var initial = Session("initial-token");
        try
        {
            await session.SaveAsync(initial);
            var stagedKey = Preferences.Default.Get("auth_token_storage_key", string.Empty);
            Preferences.Default.Remove("auth_token_storage_key"); SecureStorage.Default.Remove(stagedKey);
            await SecureStorage.Default.SetAsync("auth_token", "legacy-token");
            Assert.Equal("legacy-token", await new AuthSessionService().GetTokenAsync());
            await session.SaveAsync(initial);
            Assert.Equal(initial.Token, await session.GetTokenAsync());
            Assert.Null(await SecureStorage.Default.GetAsync("auth_token"));
        }
        finally { session.Clear(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Verified_session_for_another_owner_or_trip_never_inherits_previous_access_expiry(bool changeOwner, bool expired)
    {
        var session = new AuthSessionService(); var initial = Session("initial-token");
        try
        {
            await session.SaveAsync(initial);
            var expiry = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1);
            session.ApplySyncState(new(new(true, true, true, true, false), expiry, initial.TripId,
                null, null, 1, 1, 1, 1, 1));
            Assert.Equal(expiry, session.KnownAccessExpiresAtUtc);
            var next = initial with
            {
                UserId = changeOwner ? Guid.NewGuid() : initial.UserId,
                TripId = changeOwner ? initial.TripId : Guid.NewGuid(),
                Token = "new-owner-or-trip-token", AccessMode = SessionAccessMode.BuilderReadOnly,
                ExperienceMode = ExperienceMode.SelfServiceBuilder,
                Capabilities = new(false, false, false, false, false, false)
            };
            await session.SaveAsync(next);
            Assert.Null(session.KnownAccessExpiresAtUtc);
            Assert.Equal(next.UserId, session.CurrentUserId);
            Assert.Equal(next.TripId, session.CurrentTripId);
            Assert.Equal(next.Token, await session.GetTokenAsync());
            Assert.False(session.CanEditItinerary);
            Assert.False(session.CanUseAssistant);
            Assert.False(session.CanCalculateRoutes);
            Assert.False(session.HasCuratedDocs);
        }
        finally { session.Clear(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Verified_update_of_same_owner_and_trip_preserves_known_expiration_restriction(bool expired)
    {
        var session = new AuthSessionService(); var initial = Session("initial-token");
        try
        {
            await session.SaveAsync(initial);
            var expiry = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1);
            session.ApplySyncState(new(new(true, true, true, true, false), expiry, initial.TripId,
                null, null, 1, 1, 1, 1, 1));
            await session.SaveAsync(initial with { Token = "same-context-updated-token", EmailVerified = true });
            Assert.Equal(expiry, session.KnownAccessExpiresAtUtc);
            Assert.Equal(!expired, session.HasKnownValidAccess);
            Assert.Equal(!expired, session.CanUseAssistant);
            Assert.Equal("same-context-updated-token", await session.GetTokenAsync());
        }
        finally { session.Clear(); }
    }

    private static AuthSessionDto Session(string token) => new(Guid.NewGuid(), "synthetic@example.test", "Synthetic", false, token, Guid.NewGuid());
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
