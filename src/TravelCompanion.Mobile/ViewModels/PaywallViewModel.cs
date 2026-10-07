using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
    private bool _detailsExpanded;
    private PurchaseContext? _offerContext;
    private bool _loadingOffer;

    private sealed class PurchaseContext(Guid userId, Guid tripId, long version)
    {
        public Guid UserId { get; private set; } = userId;
        public Guid TripId { get; } = tripId;
        public long Version { get; private set; } = version;
        public bool IsCurrent(AuthSessionService session, CancellationToken ct) => !ct.IsCancellationRequested
            && session.HasSession && session.CurrentUserId == UserId && session.CurrentTripId == TripId
            && session.ContextVersion == Version;
        public bool AdoptSession(AuthSessionService session, AuthSessionDto applied, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || !session.HasSession || session.CurrentUserId != applied.UserId
                || session.CurrentTripId != TripId || applied.TripId != TripId) return false;
            UserId = applied.UserId;
            Version = session.ContextVersion;
            return true;
        }
    }

    private PurchaseContext? CaptureContext() => sessions.HasSession && sessions.CurrentUserId is { } userId
        && sessions.CurrentTripId is { } tripId ? new(userId, tripId, sessions.ContextVersion) : null;

    private Task ScopedLoadAsync(Func<PurchaseContext, CancellationToken, Task> action)
    {
        var context = CaptureContext();
        if (context is null) return Task.CompletedTask;
        return LoadAsync(async ct =>
        {
            try { await action(context, ct); }
            catch (Exception) when (!context.IsCurrent(sessions, ct)) { }
        });
    }

    public void CancelOfferLoad() { if (_loadingOffer) CancelLoading(); }
    public ObservableCollection<string> Benefits { get; } = [];
    public string DisplayPrice { get => _displayPrice; private set { if (SetProperty(ref _displayPrice, value)) OnPropertyChanged(nameof(HasPrice)); } }
    public bool HasPrice => !string.IsNullOrWhiteSpace(DisplayPrice);
    public string OfferLeadText => _offer?.Benefits.FirstOrDefault() ?? string.Empty;
    public string HeroTitle => Resource(PaywallPresentation.HeadingKey(_entryPoint));
    public string HeroBody => HasContextualLead ? OfferLeadText : Resource("PaywallHeroBody");
    public bool DetailsExpanded => _detailsExpanded && HasOffer;
    public string DetailsAction => Resource(DetailsExpanded ? "PaywallHideDetails" : "PaywallShowDetails");
    public bool NeedsTripSetup => sessions.CurrentTripId is null;
    public bool HasOffer => _offer is not null;
    public bool HasContextualLead => HasOffer && _entryPoint != PaywallEntryPoint.ExplicitUpgrade;
    public bool HasRetention => !string.IsNullOrWhiteSpace(RetentionText);
    public bool HasAccessDetails => _offer?.PassExpiresAtUtc is not null;
    public bool HasState => !string.IsNullOrWhiteSpace(StateText);
    public bool ShowRetry => !IsBusy && !CanBuy && !NeedsTripSetup && (_offer?.CanPurchase ?? true);
    protected override void OnLoadStateChanged() { OnPropertyChanged(nameof(CanBuy)); OnPropertyChanged(nameof(ShowRetry)); }
    public bool CanBuy { get => _canBuy && !IsBusy && _offerContext?.IsCurrent(sessions, CancellationToken.None) == true; private set { SetProperty(ref _canBuy, value); OnPropertyChanged(nameof(ShowRetry)); } }
    public string StateText { get => _stateText; private set { if (SetProperty(ref _stateText, value)) OnPropertyChanged(nameof(HasState)); } }
    public string PreviewText => _offer is null ? string.Empty
        : _offer.ItemCount == 0 ? Resource("PaywallNewTrip")
        : string.Format(Resource("PaywallPreviewFormat"), _offer.ItemCount, _offer.PlannedDays.Count, _offer.SavedRouteCount);
    public string RetentionText => _offer?.DraftDeletionAtUtc is { } deletion
        ? string.Format(Resource("PaywallRetentionFormat"), deletion.ToLocalTime()) : string.Empty;
    public string AccessDetailsText => _offer is null ? string.Empty
        : string.Format(Resource("PaywallAccessDetailsFormat"),
            _offer.PassExpiresAtUtc?.ToLocalTime(), _offer.DailyAssistantLimit,
            _offer.AssistantQuotaResetsAtUtc?.ToLocalTime());

    private void NotifyOffer()
    {
        OnPropertyChanged(nameof(ShowRetry));
        foreach (var property in new[] { nameof(HasOffer), nameof(NeedsTripSetup), nameof(HasContextualLead), nameof(OfferLeadText), nameof(PreviewText), nameof(RetentionText), nameof(AccessDetailsText), nameof(HasRetention), nameof(HasAccessDetails), nameof(HeroTitle), nameof(HeroBody), nameof(DetailsExpanded), nameof(DetailsAction) })
            OnPropertyChanged(property);
    }

    public void SetEntryPoint(string? value)
    {
        if (Enum.TryParse<PaywallEntryPoint>(value, out var parsed)) _entryPoint = parsed;
        NotifyOffer();
    }

    [RelayCommand]
    private void ToggleDetails()
    {
        _detailsExpanded = !_detailsExpanded;
        OnPropertyChanged(nameof(DetailsExpanded));
        OnPropertyChanged(nameof(DetailsAction));
    }

    [RelayCommand]
    private async Task LoadOfferAsync()
    {
        if (IsBusy) return;
        Benefits.Clear();
        CanBuy = false;
        DisplayPrice = string.Empty;
        StateText = string.Empty;
        _offer = null;
        _offerContext = null;
        NotifyOffer();
        _loadingOffer = true;
        try
        {
            await ScopedLoadAsync(async (context, ct) =>
            {
                var entryPoint = _entryPoint;
                var token = await sessions.GetTokenAsync();
                if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(token)) return;
                var tripId = context.TripId;
                var platform = DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : DeviceInfo.Platform == DevicePlatform.Android ? "android" : "desktop";
                var offer = await api.GetPaywallOfferAsync(token, tripId, entryPoint, platform, ct);
                if (!context.IsCurrent(sessions, ct) || _entryPoint != entryPoint) return;
                _offer = offer ?? throw new InvalidOperationException(Resource("PaywallLoadError"));
                _offerContext = context;
                NotifyOffer();
                Benefits.Clear(); foreach (var benefit in _offer.Benefits) Benefits.Add(benefit);
                await TrackAsync("paywall_shown", tripId, _offer.Variant, ct);
                if (!context.IsCurrent(sessions, ct)) return;
                var product = await store.GetProductAsync(_offer.ProductId, ct);
                if (!context.IsCurrent(sessions, ct)) return;
                var presentation = PaywallPricePresentation.Create(_offer.CanPurchase, product.IsAvailable, product.LocalizedPrice);
                DisplayPrice = presentation.Price;
                CanBuy = presentation.CanBuy;
                StateText = !_offer.CanPurchase ? Resource("PaywallPurchasesPaused")
                    : _canBuy ? string.Empty : product.Error ?? Resource("PaywallUnavailable");
                OnPropertyChanged(nameof(PreviewText)); OnPropertyChanged(nameof(RetentionText));
                OnPropertyChanged(nameof(AccessDetailsText));
                await ResumePendingPurchaseAsync(token, context, ct);
            });
        }
        finally
        {
            _loadingOffer = false;
            if (_offerContext is not null && !_offerContext.IsCurrent(sessions, CancellationToken.None))
            {
                _offer = null; _offerContext = null; CanBuy = false;
                Benefits.Clear(); DisplayPrice = string.Empty; StateText = string.Empty;
                NotifyOffer();
            }
        }
    }

    [RelayCommand]
    private Task ConfigureTripAsync() => BuilderSetupNavigation.OpenAsync();

    [RelayCommand]
    private Task BuyAsync() => ScopedLoadAsync(async (context, ct) =>
    {
        if (!_canBuy || _offer is not { } offer || _offerContext?.IsCurrent(sessions, ct) != true) return;
        var tripId = context.TripId;
        var token = await sessions.GetTokenAsync();
        if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(token)) return;
        await TrackAsync("paywall_cta_selected", tripId, offer.Variant, ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (!sessions.EmailVerified)
        {
            var email = await Shell.Current.DisplayPromptAsync(Resource("PaywallRecoverTitle"), Resource("PaywallRecoverPrompt"), Resource("AccountSendCode"), Resource("CommonCancel"), keyboard: Keyboard.Email);
            if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(email)) return;
            StateText = Resource("PaywallSendingCode");
            await TrackAsync("email_verification_started", tripId, offer.Variant, ct);
            if (!context.IsCurrent(sessions, ct)) return;
            var requested = await api.RequestEmailCodeAsync(token, email, System.Globalization.CultureInfo.CurrentUICulture.Name, ct);
            if (!context.IsCurrent(sessions, ct)) return;
            if (requested is null)
                throw new InvalidOperationException(Resource("AccountCodeSendError"));
            var code = await Shell.Current.DisplayPromptAsync(Resource("AccountVerifyTitle"), Resource("AccountVerifyPrompt"), Resource("AccountVerify"), Resource("CommonCancel"), keyboard: Keyboard.Numeric, maxLength: 6);
            if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(code)) return;
            var verified = await api.VerifyEmailCodeAsync(token, email, new string(code.Where(char.IsDigit).ToArray()), ct);
            if (!context.IsCurrent(sessions, ct)) return;
            if (verified is null) throw new InvalidOperationException(Resource("AccountCodeInvalid"));
            await MauiProgram.Services.GetRequiredService<JournalStore>().TransferLinkedTripAsync(verified);
            if (!context.IsCurrent(sessions, ct)) return;
            await MauiProgram.Services.GetRequiredService<ExpenseStore>().TransferLinkedTripAsync(verified);
            if (!context.IsCurrent(sessions, ct)) return;
            await logout.ResetContentAsync(context.UserId, preservePendingItineraryAction: true);
            if (!context.IsCurrent(sessions, ct)) return;
            if (!await sessions.SaveIfCurrentAsync(verified, context.Version)) return;
            token = verified.Token;
            if (!context.AdoptSession(sessions, verified, ct)) return;
            _offerContext = context;
        }
        StateText = Resource("PaywallPreparing");
        var intent = await api.CreatePurchaseIntentAsync(token, new(tripId, store.Provider, offer.ProductId,
            _entryPoint, offer.Variant, System.Globalization.CultureInfo.CurrentUICulture.Name,
            AppInfo.Current.VersionString, DeviceInfo.Platform.ToString()), ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (intent is null) throw new InvalidOperationException(Resource("PaywallPrepareError"));
        var pending = new PendingStorePurchase(context.UserId, tripId, intent.Id, intent.Provider, intent.ProductId,
            intent.OpaqueAccountId, StoreEnvironment.Production, null, DateTimeOffset.UtcNow);
        await pendingPurchases.SaveAsync(pending);
        if (!context.IsCurrent(sessions, ct)) return;
        StateText = Resource("PaywallAwaitingConfirmation");
        var purchase = await store.PurchaseAsync(intent.ProductId, intent.OpaqueAccountId, ct);
        if (purchase.State == PurchaseIntentState.Cancelled)
        {
            if (!context.IsCurrent(sessions, ct)) return;
            await api.CancelPurchaseIntentAsync(token, intent.Id, ct);
            if (!context.IsCurrent(sessions, ct)) return;
            await pendingPurchases.ClearAsync(pending);
            if (!context.IsCurrent(sessions, ct)) return;
            StateText = Resource("PaywallCancelled");
            return;
        }
        if (purchase.State == PurchaseIntentState.Pending && string.IsNullOrWhiteSpace(purchase.Evidence))
        {
            if (context.IsCurrent(sessions, ct)) StateText = Resource("PaywallPending");
            return;
        }
        if (string.IsNullOrWhiteSpace(purchase.Evidence))
        {
            if (!context.IsCurrent(sessions, ct)) return;
            throw new InvalidOperationException(purchase.Error ?? Resource("PaywallEvidenceError"));
        }
        // A native purchase can finish after navigation/account changes. Keep its receipt
        // under the original owner, then let reconciliation resume on that account.
        pending = pending with { Environment = purchase.Environment, Evidence = purchase.Evidence, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await pendingPurchases.SaveAsync(pending);
        if (!context.IsCurrent(sessions, ct)) return;
        StateText = Resource("PaywallVerifying");
        await VerifyAndActivateAsync(token, pending, context, ct);
    });

    private async Task TrackAsync(string name, Guid tripId, string variant, CancellationToken ct)
    {
        await analytics.TrackAsync(name, _entryPoint.ToString(), variant, tripId, ct);
    }

    [RelayCommand]
    private Task RestoreAsync() => ScopedLoadAsync(async (context, ct) =>
    {
        var token = await sessions.GetTokenAsync();
        if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(token)) return;
        var restoring = Resource("PaywallRestoring");
        var lookingForPending = Resource("PaywallLookingForPending");
        StateText = restoring;
        var passes = await api.RestorePassesAsync(token, ct);
        if (!context.IsCurrent(sessions, ct)) return;
        var tripId = context.TripId;
        if (passes.Any(item => item.TripId == tripId && item.State == TrialAccessState.Paid))
        {
            var selected = await api.SelectAccountTripAsync(token, tripId, ct);
            if (!context.IsCurrent(sessions, ct)) return;
            if (selected is null) throw new InvalidOperationException(Resource("PaywallLoadError"));
            if (selected.UserId != context.UserId || selected.TripId != context.TripId) return;
            if (!await sessions.SaveIfCurrentAsync(selected, context.Version)) return;
            if (!context.AdoptSession(sessions, selected, ct)) return;
            if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
            StateText = Resource("PaywallRestored");
            await ResumeOriginAsync(context, ct);
            return;
        }
        await ResumePendingPurchaseAsync(token, context, ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (StateText == restoring || StateText == lookingForPending)
            StateText = Resource("PaywallNoActivePass");
    });

    private async Task ResumePendingPurchaseAsync(string token, PurchaseContext context, CancellationToken ct)
    {
        var pending = await pendingPurchases.GetAsync(context.UserId, context.TripId);
        if (!context.IsCurrent(sessions, ct) || pending is null || pending.UserId != context.UserId || pending.TripId != context.TripId
            || pending.Provider != store.Provider)
            return;

        if (!string.IsNullOrWhiteSpace(pending.Evidence))
        {
            StateText = Resource("PaywallRecoveringPurchase");
            await VerifyAndActivateAsync(token, pending, context, ct);
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
        if (!context.IsCurrent(sessions, ct)) return;
        await VerifyAndActivateAsync(token, pending, context, ct);
    }

    private async Task VerifyAndActivateAsync(string token, PendingStorePurchase pending, PurchaseContext context, CancellationToken ct)
    {
        var pass = await api.VerifyPurchaseAsync(token,
            new(pending.IntentId, pending.Evidence!, pending.Environment, $"resume-{pending.IntentId:N}"), ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (pass is null) throw new InvalidOperationException(Resource("PaywallVerifyError"));
        if (pass.TripId != context.TripId) return;
        if (pass.State == TrialAccessState.PurchasePending)
        {
            StateText = Resource("PaywallPending");
            return;
        }
        if (pass.State != TrialAccessState.Paid) throw new InvalidOperationException(Resource("PaywallNotActive"));
        await store.FinishAsync(pending.Evidence!, ct);
        if (!context.IsCurrent(sessions, ct)) return;
        var selected = await api.SelectAccountTripAsync(token, pending.TripId, ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (selected is null) throw new InvalidOperationException(Resource("PaywallLoadError"));
        if (selected.UserId != context.UserId || selected.TripId != context.TripId) return;
        if (!await sessions.SaveIfCurrentAsync(selected, context.Version)) return;
        if (!context.AdoptSession(sessions, selected, ct)) return;
        await pendingPurchases.ClearAsync(pending);
        if (!context.IsCurrent(sessions, ct)) return;
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        StateText = Resource("PaywallActive");
        await ResumeOriginAsync(context, ct);
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

    private async Task ResumeOriginAsync(PurchaseContext context, CancellationToken ct)
    {
        await analytics.TrackAsync("paid_trip_opened", "purchase", tripId: context.TripId, cancellationToken: ct);
        if (!context.IsCurrent(sessions, ct)) return;
        var pending = pendingActions.TakeAction();
        if (pending is not null)
        {
            var parameters = new ShellNavigationQueryParameters { ["Recommendation"] = pending.Recommendation };
            if (pending.Date.HasValue) parameters["Date"] = pending.Date.Value;
            if (pending.SuggestedStartTime.HasValue) parameters["SuggestedStartTime"] = pending.SuggestedStartTime.Value;
            await Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), parameters);
            return;
        }
        var adaptation = pendingActions.TakeAdaptation();
        if (adaptation is not null)
        {
            await Shell.Current.GoToAsync("//main/assistant", new ShellNavigationQueryParameters
            {
                ["ReviewDate"] = adaptation.Date,
                ["ReviewCity"] = adaptation.City ?? string.Empty,
                ["AdaptationReason"] = adaptation.Reason,
                ["DelayMinutes"] = adaptation.DelayMinutes ?? 0
            });
            return;
        }
        var personalization = pendingActions.TakePersonalization();
        if (personalization is not null)
        {
            await Shell.Current.GoToAsync("//main/assistant", new ShellNavigationQueryParameters
            {
                ["ReviewDate"] = personalization.Date,
                ["ReviewCity"] = personalization.City ?? string.Empty,
                ["PersonalizedCriteria"] = personalization.Criteria
            });
            return;
        }
        await (_entryPoint switch
        {
            PaywallEntryPoint.Map => Shell.Current.GoToAsync("//main/map"),
            PaywallEntryPoint.Assistant => Shell.Current.GoToAsync("//main/assistant"),
            PaywallEntryPoint.Routes => Shell.Current.GoToAsync("//main/schedule"),
            PaywallEntryPoint.Expenses => Shell.Current.GoToAsync("//main/schedule", new ShellNavigationQueryParameters { ["ShowExpenses"] = true }),
            _ => Shell.Current.GoToAsync("//main/schedule")
        });
    }

    [RelayCommand]
    private Task RedeemCodeAsync() => ScopedLoadAsync(async (context, ct) =>
    {
        var pin = await Shell.Current.DisplayPromptAsync(Resource("PaywallAdminCodeTitle"), Resource("PaywallAdminCodePrompt"), Resource("PaywallActivate"), Resource("CommonCancel"), keyboard: Keyboard.Numeric, maxLength: 6);
        if (!context.IsCurrent(sessions, ct) || string.IsNullOrWhiteSpace(pin)) return;
        var token = await sessions.GetTokenAsync();
        if (!context.IsCurrent(sessions, ct)) return;
        var result = string.IsNullOrWhiteSpace(token) ? null : await api.RedeemTravelPassAsync(token, new string(pin.Where(char.IsDigit).ToArray()), ct);
        if (!context.IsCurrent(sessions, ct)) return;
        if (result is null) { ErrorMessage = Resource("PaywallAdminCodeInvalid"); return; }
        if (result.UserId != context.UserId || result.TripId != context.TripId) return;
        if (!await sessions.SaveIfCurrentAsync(result, context.Version)) return;
        if (!context.AdoptSession(sessions, result, ct)) return;
        if (Shell.Current is AppShell shell) shell.ApplySessionTabs(sessions);
        await Shell.Current.GoToAsync("//main/schedule");
    });

    private static string Resource(string key) => LocalizationResourceManager.Instance.GetString(key);
}
