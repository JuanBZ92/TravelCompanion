using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class AuthSessionLogoutTests
{
    [Theory]
    [InlineData(TravelCompanion.Shared.FreeAccessPolicy.TimedTrial, false)]
    [InlineData(TravelCompanion.Shared.FreeAccessPolicy.PersistentFree, true)]
    public async Task Persistent_free_edits_without_clock_and_policy_survives_app_restart(TravelCompanion.Shared.FreeAccessPolicy policy, bool expected)
    {
        var session = new AuthSessionService();
        try
        {
            await session.SaveAsync(Session() with
            {
                AccessMode = TravelCompanion.Shared.SessionAccessMode.FreeMapPreview,
                ExperienceMode = ExperienceMode.SelfServiceBuilder,
                Capabilities = new(false, true, true, false, false),
                TrialAccess = new(true, TrialAccessState.Editing, null, null, 2, 24.99m, "EUR", null)
                    { FreePolicy = policy, DayImprovementsRemaining = 1 }
            });
            var restarted = new AuthSessionService();
            Assert.Equal(expected, restarted.CanEditItinerary);
            Assert.Equal(policy, restarted.FreePolicy);
            Assert.Equal(1, restarted.DayImprovementsRemaining);
            restarted.ApplyTrialAccess(new(true, TrialAccessState.Revoked, null, null, 0, 24.99m, "EUR", null) { FreePolicy = policy });
            Assert.False(restarted.CanEditItinerary);
        }
        finally { session.Clear(); }
    }

    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "test-token", Guid.NewGuid());

    [Theory]
    [InlineData(ExperienceMode.CuratedPremium, false)]
    [InlineData(ExperienceMode.SelfServiceBuilder, true)]
    public async Task Curated_premium_cannot_edit_even_with_stale_editing_capability(ExperienceMode mode, bool expected)
    {
        var session = new AuthSessionService();
        try
        {
            await session.SaveAsync(Session() with
            {
                ExperienceMode = mode,
                Capabilities = new(true, true, true, false, false, true)
            });
            Assert.Equal(expected, session.CanEditItinerary);
        }
        finally { session.Clear(); }
    }

    [Fact]
    public async Task Logout_disables_biometrics_and_token_before_cleanup_even_after_restart()
    {
        var session = new AuthSessionService();
        await session.SaveAsync(Session());
        Assert.True(session.IsBiometricEnabled);
        var version = session.ContextVersion;
        session.BeginLogout();
        Assert.True(session.ContextVersion > version);
        // Native navigation/cleanup can fail here. Persisted account data still exists.
        Assert.NotNull(session.CurrentUserId);
        var restarted = new AuthSessionService();
        Assert.False(restarted.HasSession);
        Assert.False(restarted.IsBiometricEnabled);
        Assert.Null(await restarted.GetTokenAsync());
        session.Clear();
    }

    [Fact]
    public async Task New_explicit_login_reenables_session_after_logout()
    {
        var session = new AuthSessionService();
        await session.SaveAsync(Session());
        session.BeginLogout();
        session.Clear();
        await session.SaveAsync(Session());
        Assert.True(session.HasSession);
        Assert.Equal("test-token", await session.GetTokenAsync());
        session.Clear();
    }

    [Fact]
    public async Task Clear_removes_credentials_and_is_repeatable()
    {
        var session = new AuthSessionService();
        await session.SaveAsync(Session());
        session.Clear();
        session.Clear();
        Assert.False(session.HasSession);
        Assert.False(session.IsBiometricEnabled);
        Assert.Null(session.CurrentUserId);
        Assert.Null(await SecureStorage.Default.GetAsync("auth_token"));
    }
}
