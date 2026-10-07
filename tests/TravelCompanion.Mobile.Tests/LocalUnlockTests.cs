using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class LocalUnlockTests
{
    [Fact]
    public async Task Pin_choice_remains_protected_and_survives_session_updates_password_change_and_restart()
    {
        var session = new AuthSessionService();
        var dto = Session();
        try
        {
            await session.SaveAsync(dto);
            session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            var version = session.ContextVersion;
            await session.SaveAsync(dto with { TripId = Guid.NewGuid(), MustChangePassword = true });
            Assert.Equal(DeviceUnlockMethod.Pin, session.PreferredUnlockMethod);
            Assert.Equal("//change-password", LocalUnlockRouting.StartupRoute(session, "//main/schedule"));
            session.MarkPasswordChanged();

            var restarted = new AuthSessionService();
            Assert.True(restarted.RequiresLocalUnlock);
            Assert.False(restarted.IsBiometricEnabled);
            Assert.Equal(DeviceUnlockMethod.Pin, restarted.PreferredUnlockMethod);
            Assert.Equal("//biometric-unlock", LocalUnlockRouting.StartupRoute(restarted, "//main/schedule"));
            Assert.True(session.ContextVersion > version);
        }
        finally { session.DeleteUnlockPreference(dto.UserId); session.Clear(); }
    }

    [Fact]
    public async Task Preferences_are_per_account_and_logout_does_not_erase_the_users_choice()
    {
        var session = new AuthSessionService();
        var first = Session();
        var second = Session();
        try
        {
            await session.SaveAsync(first);
            session.IsBiometricEnabled = false;
            Assert.True(session.RequiresLocalUnlock);
            session.Clear();
            Assert.Equal("//login", LocalUnlockRouting.StartupRoute(session, "//main/schedule"));
            await session.SaveAsync(second);
            Assert.Equal(DeviceUnlockMethod.Biometric, session.PreferredUnlockMethod);
            Assert.True(session.IsBiometricEnabled);
            session.Clear();
            await session.SaveAsync(first);
            Assert.Equal(DeviceUnlockMethod.Pin, session.PreferredUnlockMethod);
            Assert.True(session.RequiresLocalUnlock);
        }
        finally
        {
            session.DeleteUnlockPreference(first.UserId);
            session.DeleteUnlockPreference(second.UserId);
            session.Clear();
        }
    }

    [Fact]
    public async Task Explicit_free_user_choice_requires_unlock_and_invalid_values_never_disable_protection()
    {
        var session = new AuthSessionService();
        var dto = Session() with { AccessMode = SessionAccessMode.FreeMapPreview };
        try
        {
            await session.SaveAsync(dto);
            Assert.False(session.RequiresLocalUnlock); // Preserve the existing free default.
            session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            await session.SaveAsync(dto);
            Assert.True(session.RequiresLocalUnlock);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.SetUnlockMethod((DeviceUnlockMethod)99));
            Assert.Equal(DeviceUnlockMethod.Pin, session.PreferredUnlockMethod);

            Preferences.Default.Set("auth_unlock_method_" + dto.UserId.ToString("N"), "99");
            Assert.Equal(DeviceUnlockMethod.Biometric, session.PreferredUnlockMethod);
            Assert.True(session.RequiresLocalUnlock);
        }
        finally { session.DeleteUnlockPreference(dto.UserId); session.Clear(); }
    }

    [Fact]
    public async Task Account_deletion_removes_only_that_accounts_preference()
    {
        var session = new AuthSessionService();
        var first = Session();
        var second = Session();
        try
        {
            await session.SaveAsync(first);
            session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            await session.SaveAsync(second);
            session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            session.DeleteUnlockPreference(first.UserId);
            Assert.Equal(DeviceUnlockMethod.Pin, session.PreferredUnlockMethod);
            await session.SaveAsync(first);
            Assert.Equal(DeviceUnlockMethod.Biometric, session.PreferredUnlockMethod);
        }
        finally { session.DeleteUnlockPreference(first.UserId); session.DeleteUnlockPreference(second.UserId); session.Clear(); }
    }

    [Fact]
    public async Task Pin_startup_goes_to_verification_without_native_biometrics_or_authenticated_navigation()
    {
        var session = new AuthSessionService();
        var dto = Session();
        Shell.Current = new();
        try
        {
            await session.SaveAsync(dto);
            session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            var native = new BiometricUnlockService();
            var viewModel = new BiometricUnlockViewModel(native, session);
            await viewModel.TryAutoUnlockAsync();

            Assert.False(viewModel.UsesBiometrics);
            Assert.Equal(0, native.AvailabilityCalls);
            Assert.Equal(0, native.VerificationCalls);
            Assert.Equal("//login", Assert.Single(Shell.Current.Navigations).Route);
        }
        finally { session.DeleteUnlockPreference(dto.UserId); session.Clear(); Shell.Current = new(); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Unavailable_or_rejected_biometrics_never_open_authenticated_tabs(bool available, bool verified)
    {
        var session = new AuthSessionService();
        Shell.Current = new();
        try
        {
            await session.SaveAsync(Session());
            var native = new BiometricUnlockService
            {
                Availability = () => Task.FromResult(available),
                Verification = () => Task.FromResult(verified)
            };
            var viewModel = new BiometricUnlockViewModel(native, session);
            await viewModel.TryAutoUnlockAsync();
            Assert.Empty(Shell.Current.Navigations);
            Assert.False(viewModel.IsBusy);
            Assert.Equal(available ? 1 : 0, native.VerificationCalls);
            await viewModel.UsePasswordCommand.ExecuteAsync(null);
            Assert.Equal("//login", Assert.Single(Shell.Current.Navigations).Route);
        }
        finally { session.Clear(); Shell.Current = new(); }
    }

    [Theory]
    [InlineData(BiometricUnlockOutcome.UsePin)]
    [InlineData(BiometricUnlockOutcome.Rejected)]
    [InlineData(BiometricUnlockOutcome.Failed)]
    public async Task Native_pin_alternative_returns_to_verification_while_other_failures_remain_locked(BiometricUnlockOutcome outcome)
    {
        var session = new AuthSessionService();
        Shell.Current = new();
        try
        {
            await session.SaveAsync(Session());
            var native = new BiometricUnlockService { OutcomeVerification = () => Task.FromResult(outcome) };
            var viewModel = new BiometricUnlockViewModel(native, session);
            await viewModel.TryAutoUnlockAsync();

            Assert.True(session.HasSession);
            Assert.True(session.RequiresLocalUnlock);
            Assert.Equal(DeviceUnlockMethod.Biometric, session.PreferredUnlockMethod);
            Assert.False(viewModel.IsBusy);
            if (outcome == BiometricUnlockOutcome.UsePin)
                Assert.Equal("//login", Assert.Single(Shell.Current.Navigations).Route);
            else Assert.Empty(Shell.Current.Navigations);
            Assert.Equal(outcome == BiometricUnlockOutcome.Failed, viewModel.HasError);
        }
        finally { session.Clear(); Shell.Current = new(); }
    }

    [Theory]
    [InlineData(true, BiometricUnlockOutcome.Succeeded)]
    [InlineData(false, BiometricUnlockOutcome.Succeeded)]
    [InlineData(true, BiometricUnlockOutcome.UsePin)]
    [InlineData(false, BiometricUnlockOutcome.UsePin)]
    [InlineData(true, BiometricUnlockOutcome.Rejected)]
    [InlineData(false, BiometricUnlockOutcome.Rejected)]
    [InlineData(true, BiometricUnlockOutcome.Failed)]
    [InlineData(false, BiometricUnlockOutcome.Failed)]
    public async Task Context_or_method_change_during_native_verification_discards_the_result(bool changeAccount, BiometricUnlockOutcome outcome)
    {
        var session = new AuthSessionService();
        var first = Session();
        Shell.Current = new();
        try
        {
            await session.SaveAsync(first);
            var nativeResult = new TaskCompletionSource<BiometricUnlockOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var native = new BiometricUnlockService { OutcomeVerification = () => { started.SetResult(); return nativeResult.Task; } };
            var viewModel = new BiometricUnlockViewModel(native, session);
            var operation = viewModel.TryAutoUnlockAsync();
            await started.Task;
            if (changeAccount) await session.SaveAsync(Session());
            else session.SetUnlockMethod(DeviceUnlockMethod.Pin);
            nativeResult.SetResult(outcome);
            await operation;
            Assert.Empty(Shell.Current.Navigations);
            Assert.False(viewModel.HasError);
        }
        finally { session.DeleteUnlockPreference(first.UserId); session.Clear(); Shell.Current = new(); }
    }

    private static AuthSessionDto Session() => new(Guid.NewGuid(), "unlock@example.com", "Unlock", false, "saved-token", Guid.NewGuid());
}
