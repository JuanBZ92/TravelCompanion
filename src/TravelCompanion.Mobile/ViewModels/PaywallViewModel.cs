using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class PaywallViewModel(
    TravelCompanionApiClient api,
    AuthSessionService sessions,
    SessionLogoutService logout,
    IStorePurchaseService store,
    ProductAnalyticsTracker analytics,
    PendingStorePurchaseStore pendingPurchases,
    PendingItineraryActionStore pendingActions) : ViewModelBase
{
    private PaywallOfferDto? _offer;
    private PaywallEntryPoint _entryPoint = PaywallEntryPoint.ExplicitUpgrade;
    private string _displayPrice = string.Empty;
    private bool _canBuy;
    private string _stateText = string.Empty;
    public ObservableCollection<string> Benefits { get; } = [];
    public string DisplayPrice { get => _displayPrice; private set { if (SetProperty(ref _displayPrice, value)) OnPropertyChanged(nameof(HasPrice)); } }
    public bool HasPrice => !string.IsNullOrWhiteSpace(DisplayPrice);
    public string OfferLeadText => _offer?.Benefits.FirstOrDefault() ?? string.Empty;
    public bool NeedsTripSetup => sessions.CurrentTripId is null;
    public bool HasOffer => _offer is not null;
    public bool HasRetention => !string.IsNullOrWhiteSpace(RetentionText);
    public bool HasAccessDetails => _offer?.PassExpiresAtUtc is not null;
    public bool HasState => !string.IsNullOrWhiteSpace(StateText);
    public bool ShowRetry => !IsBusy && !CanBuy && !NeedsTripSetup;
    protected override void OnLoadStateChanged() { OnPropertyChanged(nameof(CanBuy)); OnPropertyChanged(nameof(ShowRetry)); }
    public bool CanBuy { get => _canBuy && !IsBusy; private set { SetProperty(ref _canBuy, value); OnPropertyChanged(nameof(ShowRetry)); } }
    public string StateText { get => _stateText; private set { if (SetProperty(ref _stateText, value)) OnPropertyChanged(nameof(HasState)); } }
    public string PreviewText => _offer is null ? string.Empty
        : string.Format(Resource("PaywallPreviewFormat"), _offer.ItemCount, _offer.PlannedDays.Count, _offer.SavedRouteCount);
    public string RetentionText => _offer?.DraftDeletionAtUtc is { } deletion
        ? string.Format(Resource("PaywallRetentionFormat"), deletion.ToLocalTime()) : string.Empty;
    public string AccessDetailsText => _offer is null ? string.Empty
        : string.Format(Resource("PaywallAccessDetailsFormat"),
            _offer.PassExpiresAtUtc?.ToLocalTime(), _offer.DailyAssistantLimit,
            _offer.AssistantQuotaResetsAtUtc?.ToLocalTime());

    private void NotifyOffer()
    {
        foreach (var property in new[] { nameof(HasOffer), nameof(NeedsTripSetup), nameof(OfferLeadText), nameof(PreviewText), nameof(RetentionText), nameof(AccessDetailsText), nameof(HasRetention), nameof(HasAccessDetails) })
            OnPropertyChanged(property);
    }

    public void SetEntryPoint(string? value)
    {
        if (Enum.TryParse<PaywallEntryPoint>(value, out var parsed)) _entryPoint = parsed;
    }

    [RelayCommand]
    private Task LoadOfferAsync() => LoadAsync(async ct =>
    {
        Benefits.Clear();
        CanBuy = false;
        DisplayPrice = string.Empty;
        StateText = string.Empty;
        _offer = null;
        NotifyOffer();
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        if (sessions.CurrentTripId is not { } tripId)
        {
            StateText = Resource("CommerceConfigureTrip");
            return;
        }
        var platform = DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : DeviceInfo.Platform == DevicePlatform.Android ? "android" : "desktop";
        _offer = await api.GetPaywallOfferAsync(token, tripId, _entryPoint, platform, ct)
            ?? throw new InvalidOperationException(Resource("PaywallLoadError"));
        NotifyOffer();
        await TrackAsync("paywall_shown", tripId, _offer.Variant, ct);
        var product = await store.GetProductAsync(_offer.ProductId, ct);
        var presentation = PaywallPricePresentation.Create(_offer.CanPurchase, product.IsAvailable, product.LocalizedPrice);
        DisplayPrice = presentation.Price;
        CanBuy = presentation.CanBuy;
        Benefits.Clear(); foreach (var benefit in _offer.Benefits) Benefits.Add(benefit);
        StateText = _canBuy ? Resource("PaywallReady") : product.Error ?? Resource("PaywallUnavailable");
        OnPropertyChanged(nameof(PreviewText)); OnPropertyChanged(nameof(RetentionText));
        OnPropertyChanged(nameof(AccessDetailsText));
        await ResumePendingPurchaseAsync(token, tripId, ct);
    });

    [RelayCommand]
    private Task ConfigureTripAsync() => Shell.Current.GoToAsync(nameof(BuilderSetupPage));

    [RelayCommand]
    private Task BuyAsync() => LoadAsync(async ct =>
    {
        if (!_canBuy || _offer is null || sessions.CurrentTripId is not { } tripId) return;
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        await TrackAsync("paywall_cta_selected", tripId, _offer.Variant, ct);
        if (!sessions.EmailVerified)
        {
            var email = await Shell.Current.DisplayPromptAsync(Resource("PaywallRecoverTitle"), Resource("PaywallRecoverPrompt"), Resource("AccountSendCode"), Resource("CommonCancel"), keyboard: Keyboard.Email);
            if (string.IsNullOrWhiteSpace(email)) return;
            StateText = Resource("PaywallSendingCode");
            await TrackAsync("email_verification_started", tripId, _offer.Variant, ct);
            if (await api.RequestEmailCodeAsync(token, email, System.Globalization.CultureInfo.CurrentUICulture.Name, ct) is null)
                throw new InvalidOperationException(Resource("AccountCodeSendError"));
            var code = await Shell.Current.DisplayPromptAsync(Resource("AccountVerifyTitle"), Resource("AccountVerifyPrompt"), Resource("AccountVerify"), Resource("CommonCancel"), keyboard: Keyboard.Numeric, maxLength: 6);
            if (string.IsNullOrWhiteSpace(code)) return;
            var verified = await api.VerifyEmailCodeAsync(token, email, new string(code.Where(char.IsDigit).ToArray()), ct)
                ?? throw new InvalidOperationException(Resource("AccountCodeInvalid"));
            await logout.ResetContentAsync(sessions.CurrentUserId, preservePendingItineraryAction: true);
            await sessions.SaveAsync(verified); token = verified.Token;
        }
        StateText = Resource("PaywallPreparing");
        var intent = await api.CreatePurchaseIntentAsync(token, new(tripId, store.Provider, _offer.ProductId,
            _entryPoint, _offer.Variant, System.Globalization.CultureInfo.CurrentUICulture.Name,
            AppInfo.Current.VersionString, DeviceInfo.Platform.ToString()), ct)
            ?? throw new InvalidOperationException(Resource("PaywallPrepareError"));
        await pendingPurchases.SaveAsync(new PendingStorePurchase(
            sessions.CurrentUserId!.Value, tripId, intent.Id, intent.Provider, intent.ProductId,
            intent.OpaqueAccountId, StoreEnvironment.Production, null, DateTimeOffset.UtcNow));
        StateText = Resource("PaywallAwaitingConfirmation");
        var purchase = await store.PurchaseAsync(intent.ProductId, intent.OpaqueAccountId, ct);
        if (purchase.State == PurchaseIntentState.Cancelled)
        {
            await api.CancelPurchaseIntentAsync(token, intent.Id, ct);
            pendingPurchases.Clear();
            StateText = Resource("PaywallCancelled");
            return;
        }
        if (purchase.State == PurchaseIntentState.Pending && string.IsNullOrWhiteSpace(purchase.Evidence))
        {
            StateText = Resource("PaywallPending");
            return;
        }
        if (string.IsNullOrWhiteSpace(purchase.Evidence)) throw new InvalidOperationException(purchase.Error ?? Resource("PaywallEvidenceError"));
        await pendingPurchases.SaveAsync(new PendingStorePurchase(
            sessions.CurrentUserId!.Value, tripId, intent.Id, intent.Provider, intent.ProductId,
            intent.OpaqueAccountId, purchase.Environment, purchase.Evidence, DateTimeOffset.UtcNow));
        StateText = Resource("PaywallVerifying");
        await VerifyAndActivateAsync(token, await pendingPurchases.GetAsync() ?? throw new InvalidOperationException(), ct);
    });

    private async Task TrackAsync(string name, Guid tripId, string variant, CancellationToken ct)
    {
        await analytics.TrackAsync(name, _entryPoint.ToString(), variant, tripId, ct);
    }

    [RelayCommand]
    private Task RestoreAsync() => LoadAsync(async ct =>
    {
        var token = await sessions.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var restoring = Resource("PaywallRestoring");
        var lookingForPending = Resource("PaywallLookingForPending");
        StateText = restoring;
        var passes = await api.RestorePassesAsync(token, ct);
        if (sessions.CurrentTripId is { } tripId && passes.Any(item => item.TripId == tripId && item.State == TrialAccessState.Paid))
        {
            var selected = await api.SelectAccountTripAsync(token, tripId, ct);
            if (selected is not null) await sessions.SaveAsync(selected);
            if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
            StateText = Resource("PaywallRestored");
            await ResumeOriginAsync();
            return;
        }
        if (sessions.CurrentTripId is { } pendingTripId)
            await ResumePendingPurchaseAsync(token, pendingTripId, ct);
        if (StateText == restoring || StateText == lookingForPending)
            StateText = Resource("PaywallNoActivePass");
    });

    private async Task ResumePendingPurchaseAsync(string token, Guid tripId, CancellationToken ct)
    {
        var pending = await pendingPurchases.GetAsync();
        if (pending is null || pending.UserId != sessions.CurrentUserId || pending.TripId != tripId
            || pending.Provider != store.Provider)
            return;

        if (!string.IsNullOrWhiteSpace(pending.Evidence))
        {
            StateText = Resource("PaywallRecoveringPurchase");
            await VerifyAndActivateAsync(token, pending, ct);
            return;
        }

        StateText = Resource("PaywallLookingForPending");
        var restored = await store.RestoreAsync(ct);
        var evidence = restored.FirstOrDefault(item => EvidenceMatches(item, pending.OpaqueAccountId))
            ?? (restored.Count == 1 ? restored[0] : null);
        if (string.IsNullOrWhiteSpace(evidence)) return;
        pending = pending with
        {
            Evidence = evidence,
            Environment = DetectEnvironment(evidence),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await pendingPurchases.SaveAsync(pending);
        await VerifyAndActivateAsync(token, pending, ct);
    }

    private async Task VerifyAndActivateAsync(string token, PendingStorePurchase pending, CancellationToken ct)
    {
        var pass = await api.VerifyPurchaseAsync(token,
            new(pending.IntentId, pending.Evidence!, pending.Environment, Guid.NewGuid().ToString("N")), ct)
            ?? throw new InvalidOperationException(Resource("PaywallVerifyError"));
        if (pass.State == TrialAccessState.PurchasePending)
        {
            StateText = Resource("PaywallPending");
            return;
        }
        if (pass.State != TrialAccessState.Paid) throw new InvalidOperationException(Resource("PaywallNotActive"));
        await store.FinishAsync(pending.Evidence!, ct);
        pendingPurchases.Clear();
        var selected = await api.SelectAccountTripAsync(token, pending.TripId, ct);
        if (selected is not null) await sessions.SaveAsync(selected);
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        StateText = Resource("PaywallActive");
        await ResumeOriginAsync();
    }

    private static bool EvidenceMatches(string evidence, string opaqueAccountId)
    {
        try
        {
            if (evidence.Count(character => character == '.') == 2)
            {
                var payload = evidence.Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
                return document.RootElement.TryGetProperty("appAccountToken", out var appleAccount)
                    && string.Equals(appleAccount.GetString(), opaqueAccountId, StringComparison.OrdinalIgnoreCase);
            }
            using var json = JsonDocument.Parse(evidence);
            return (json.RootElement.TryGetProperty("obfuscatedAccountId", out var googleAccount)
                    || json.RootElement.TryGetProperty("obfuscatedExternalAccountId", out googleAccount))
                && string.Equals(googleAccount.GetString(), opaqueAccountId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static StoreEnvironment DetectEnvironment(string evidence)
    {
        try
        {
            if (evidence.Count(character => character == '.') != 2) return StoreEnvironment.Production;
            var payload = evidence.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("environment", out var environment)
                && string.Equals(environment.GetString(), "Sandbox", StringComparison.OrdinalIgnoreCase)
                ? StoreEnvironment.Sandbox
                : StoreEnvironment.Production;
        }
        catch
        {
            return StoreEnvironment.Production;
        }
    }

    private async Task ResumeOriginAsync()
    {
        await analytics.TrackAsync("paid_trip_opened", "purchase", tripId: sessions.CurrentTripId);
        var pending = pendingActions.TakeAction();
        if (pending is not null)
        {
            var parameters = new ShellNavigationQueryParameters { ["Recommendation"] = pending.Recommendation };
            if (pending.Date.HasValue) parameters["Date"] = pending.Date.Value;
            if (pending.SuggestedStartTime.HasValue) parameters["SuggestedStartTime"] = pending.SuggestedStartTime.Value;
            await Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), parameters);
            return;
        }
        await (_entryPoint switch
        {
            PaywallEntryPoint.Map => Shell.Current.GoToAsync("//main/map"),
            PaywallEntryPoint.Assistant => Shell.Current.GoToAsync("//main/assistant"),
            PaywallEntryPoint.Routes => Shell.Current.GoToAsync("//main/schedule"),
            _ => Shell.Current.GoToAsync("//main/schedule")
        });
    }

    [RelayCommand]
    private async Task RedeemCodeAsync()
    {
        var pin = await Shell.Current.DisplayPromptAsync(Resource("PaywallAdminCodeTitle"), Resource("PaywallAdminCodePrompt"), Resource("PaywallActivate"), Resource("CommonCancel"), keyboard: Keyboard.Numeric, maxLength: 6);
        if (string.IsNullOrWhiteSpace(pin)) return;
        var token = await sessions.GetTokenAsync();
        var result = string.IsNullOrWhiteSpace(token) ? null : await api.RedeemTravelPassAsync(token, new string(pin.Where(char.IsDigit).ToArray()));
        if (result is null) { ErrorMessage = Resource("PaywallAdminCodeInvalid"); return; }
        await sessions.SaveAsync(result);
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        await Shell.Current.GoToAsync("//main/schedule");
    }

    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}
