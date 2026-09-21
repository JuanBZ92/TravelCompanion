using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

public sealed class AuthSessionLogoutTests
{
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "test-token", Guid.NewGuid());

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
