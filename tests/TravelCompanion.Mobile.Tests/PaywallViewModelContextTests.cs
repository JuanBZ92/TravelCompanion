using System.Text.Json;
using System.Text.Json.Serialization;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class PaywallViewModelContextTests
{
    [Theory]
    [InlineData("token", false)] [InlineData("token", true)]
    [InlineData("offer", false)] [InlineData("offer", true)]
    [InlineData("product", false)] [InlineData("product", true)]
    public async Task Offer_cannot_continue_or_display_results_from_an_old_account_or_trip(string stage, bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = Signal(); var release = Signal();
        if (stage == "token") SecureStorage.Default.BeforeGet = async key => { if (key.StartsWith("auth_token", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; } };
        if (stage == "offer") fixture.Api.FetchOffer = async (trip, entry) => { entered.TrySetResult(); await release.Task; return Offer(trip, entry); };
        if (stage == "product") fixture.Store.Product = async () => { entered.TrySetResult(); await release.Task; return new("test.pass", "€10", "EUR", true); };
        try
        {
            var operation = fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.ChangeContextAsync(changeAccount);
            release.TrySetResult(); await operation;
            Assert.False(fixture.ViewModel.CanBuy);
            Assert.False(fixture.ViewModel.HasOffer);
            Assert.Empty(fixture.ViewModel.Benefits);
            Assert.Empty(fixture.ViewModel.DisplayPrice);
            Assert.False(fixture.ViewModel.HasError);
            Assert.Equal(stage == "token" ? 0 : 1, fixture.Api.OfferRequests);
            Assert.Equal(stage == "product" ? 1 : 0, fixture.Store.ProductCalls);
            Assert.Equal(0, fixture.Store.RestoreCalls);
        }
        finally { SecureStorage.Default.BeforeGet = null; release.TrySetResult(); }
    }

    [Fact]
    public async Task Leaving_the_offer_cancels_its_continuation_without_starting_store_or_purchase_recovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = Signal(); var release = Signal();
        fixture.Api.FetchOffer = async (trip, entry) => { entered.TrySetResult(); await release.Task; return Offer(trip, entry); };
        var operation = fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.ViewModel.CancelOfferLoad();
        release.TrySetResult(); await operation;
        Assert.False(fixture.ViewModel.HasOffer);
        Assert.Equal(0, fixture.Store.ProductCalls);
        Assert.Equal(0, fixture.Store.RestoreCalls);
        Assert.False(fixture.ViewModel.HasError);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_purchase_receipt_remains_owned_by_original_user_and_does_not_overwrite_another_pending_purchase(bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
        var entered = Signal(); var release = Signal();
        fixture.Store.Purchase = async () => { entered.TrySetResult(); await release.Task; return new(PurchaseIntentState.Active, "owner-a-receipt", StoreEnvironment.Sandbox); };
        var purchase = fixture.ViewModel.BuyCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var original = fixture.Initial;
        await fixture.ChangeContextAsync(changeAccount);
        var newOwner = fixture.Session.CurrentUserId!.Value;
        var other = Pending(newOwner, fixture.Session.CurrentTripId!.Value);
        await fixture.Pending.SaveAsync(other);
        release.TrySetResult(); await purchase;
        var saved = await fixture.Pending.GetAsync(original.UserId, original.TripId);
        Assert.Equal("owner-a-receipt", saved!.Evidence);
        Assert.Equal(original.UserId, saved.UserId);
        Assert.Equal(other.IntentId, (await fixture.Pending.GetAsync(newOwner, other.TripId))!.IntentId);
        Assert.Equal(0, fixture.Api.VerificationRequests);
        Assert.Equal(0, fixture.Api.SelectTripRequests);
        Assert.Empty(Shell.Current.Navigations);
        Assert.Equal(newOwner, fixture.Session.CurrentUserId);
        Assert.False(fixture.ViewModel.HasError);
    }

    [Theory]
    [InlineData("verify", false)] [InlineData("verify", true)]
    [InlineData("finish", false)] [InlineData("finish", true)]
    [InlineData("select", false)] [InlineData("select", true)]
    public async Task Verification_and_activation_never_restore_a_stale_session(string stage, bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
        var entered = Signal(); var release = Signal();
        if (stage == "verify") fixture.Api.VerifyPurchase = async _ => { entered.TrySetResult(); await release.Task; return Paid(fixture.Initial.TripId!.Value); };
        if (stage == "finish") fixture.Store.Finish = async () => { entered.TrySetResult(); await release.Task; };
        if (stage == "select") fixture.Api.SelectTrip = async _ => { entered.TrySetResult(); await release.Task; return fixture.Initial; };
        var purchase = fixture.ViewModel.BuyCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await fixture.ChangeContextAsync(changeAccount);
        release.TrySetResult(); await purchase;
        Assert.Equal(current.UserId, fixture.Session.CurrentUserId);
        Assert.Equal(current.TripId, fixture.Session.CurrentTripId);
        Assert.Equal(current.Token, await fixture.Session.GetTokenAsync());
        Assert.NotNull(await fixture.Pending.GetAsync(fixture.Initial.UserId, fixture.Initial.TripId));
        Assert.Empty(Shell.Current.Navigations);
        Assert.False(fixture.ViewModel.HasError);
        Assert.Equal(stage == "verify" ? 0 : 1, fixture.Store.FinishCalls);
        Assert.Equal(stage == "select" ? 1 : 0, fixture.Api.SelectTripRequests);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Restore_discards_old_trip_selection_after_context_changes(bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.RestorePasses = () => Task.FromResult<IReadOnlyList<PassAccessDto>>([Paid(fixture.Initial.TripId!.Value)]);
        var entered = Signal(); var release = Signal();
        fixture.Api.SelectTrip = async _ => { entered.TrySetResult(); await release.Task; return fixture.Initial; };
        var restore = fixture.ViewModel.RestoreCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await fixture.ChangeContextAsync(changeAccount);
        release.TrySetResult(); await restore;
        Assert.Equal(current.UserId, fixture.Session.CurrentUserId);
        Assert.Equal(current.TripId, fixture.Session.CurrentTripId);
        Assert.Empty(Shell.Current.Navigations);
    }

    [Theory]
    [InlineData("purchase")] [InlineData("restore")] [InlineData("recovery")]
    public async Task Logout_during_activation_token_persistence_cannot_publish_the_old_profile_or_token(string action)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
        var applied = fixture.Initial with { Token = "old-owner-activated-token" };
        fixture.Api.SelectTrip = _ => Task.FromResult<AuthSessionDto?>(applied);
        if (action == "restore") fixture.Api.RestorePasses = () => Task.FromResult<IReadOnlyList<PassAccessDto>>([Paid(applied.TripId!.Value)]);
        if (action == "recovery") await fixture.Pending.SaveAsync(Pending(applied.UserId, applied.TripId!.Value) with { Evidence = "synthetic-receipt" });
        var entered = Signal(); var release = Signal();
        SecureStorage.Default.BeforeSet = async (key, value) =>
        {
            if (key.StartsWith("auth_token", StringComparison.Ordinal) && value == applied.Token)
            { entered.TrySetResult(); await release.Task; }
        };
        try
        {
            var operation = action switch
            {
                "purchase" => fixture.ViewModel.BuyCommand.ExecuteAsync(null),
                "restore" => fixture.ViewModel.RestoreCommand.ExecuteAsync(null),
                _ => new StorePurchaseRecoveryService(fixture.Session, fixture.Api, fixture.Store, fixture.Pending, new()).RecoverAsync()
            };
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(fixture.Initial.UserId, fixture.Session.CurrentUserId);
            Assert.Equal(fixture.Initial.Token, await fixture.Session.GetTokenAsync());
            fixture.Session.BeginLogout();
            var change = fixture.ChangeContextAsync(true);
            release.TrySetResult();
            await operation; var current = await change;
            Assert.Equal(current.UserId, fixture.Session.CurrentUserId);
            Assert.Equal(current.TripId, fixture.Session.CurrentTripId);
            Assert.Equal(current.Token, await fixture.Session.GetTokenAsync());
            Assert.Empty(Shell.Current.Navigations);
            if (action != "restore") Assert.NotNull(await fixture.Pending.GetAsync(fixture.Initial.UserId));
            Assert.False(fixture.ViewModel.HasError);
        }
        finally { release.TrySetResult(); SecureStorage.Default.BeforeSet = null; }
    }

    [Fact]
    public async Task Successful_purchase_keeps_verification_restore_flow_and_clears_only_its_own_intent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ViewModel.LoadOfferCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.CanBuy);
        var other = Pending(Guid.NewGuid(), Guid.NewGuid());
        await fixture.Pending.SaveAsync(other);
        await fixture.ViewModel.BuyCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Api.VerificationRequests);
        Assert.Equal(1, fixture.Store.FinishCalls);
        Assert.Equal(1, fixture.Api.SelectTripRequests);
        Assert.Null(await fixture.Pending.GetAsync(fixture.Initial.UserId));
        Assert.Equal(other, await fixture.Pending.GetAsync(other.UserId));
        Assert.Equal("//main/schedule", Assert.Single(Shell.Current.Navigations).Route);
        Assert.False(fixture.ViewModel.HasError);
        await fixture.Pending.ClearAsync(other);
    }

    [Fact]
    public async Task Legacy_pending_purchase_survives_restart_and_other_account_writes_without_losing_receipt()
    {
        var legacy = Pending(Guid.NewGuid(), Guid.NewGuid()) with { Evidence = "legacy-receipt" };
        var other = Pending(Guid.NewGuid(), Guid.NewGuid());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        await SecureStorage.Default.SetAsync("pending_store_purchase_v1", JsonSerializer.Serialize(legacy, options));
        var store = new PendingStorePurchaseStore();
        try
        {
            Assert.Null(await store.GetAsync(other.UserId));
            await store.SaveAsync(other);
            Assert.Equal(legacy, await new PendingStorePurchaseStore().GetAsync(legacy.UserId, legacy.TripId));
            await store.SaveAsync(legacy);
            Assert.Null(await SecureStorage.Default.GetAsync("pending_store_purchase_v1"));
            await store.ClearAsync(other);
            Assert.Equal(legacy, await new PendingStorePurchaseStore().GetAsync(legacy.UserId));
        }
        finally { await store.ClearAsync(legacy); await store.ClearAsync(other); SecureStorage.Default.Remove("pending_store_purchase_v1"); }
    }

    [Fact]
    public async Task Corrupt_legacy_record_never_blocks_a_valid_receipt_and_account_cleanup_only_removes_its_owner()
    {
        var pending = Pending(Guid.NewGuid(), Guid.NewGuid()) with { Evidence = "valid-v2-receipt" };
        var other = Pending(Guid.NewGuid(), Guid.NewGuid());
        var store = new PendingStorePurchaseStore();
        try
        {
            await store.SaveAsync(pending);
            await SecureStorage.Default.SetAsync("pending_store_purchase_v1", "{broken-json");
            Assert.Equal(pending, await new PendingStorePurchaseStore().GetAsync(pending.UserId));
            await store.SaveAsync(pending with { Environment = StoreEnvironment.Production });
            Assert.Equal("valid-v2-receipt", (await store.GetAsync(pending.UserId))!.Evidence);
            await store.SaveAsync(other);
            await store.ClearUserAsync(pending.UserId);
            Assert.Null(await store.GetAsync(pending.UserId));
            Assert.Equal(other, await store.GetAsync(other.UserId));
        }
        finally { await store.ClearUserAsync(pending.UserId); await store.ClearUserAsync(other.UserId); SecureStorage.Default.Remove("pending_store_purchase_v1"); }
    }

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static PaywallOfferDto Offer(Guid trip, PaywallEntryPoint entry) => new(trip, "test.pass", null, "10", true, "test", entry, [], 0, 0, 100, null, null, 10, ["Catalog"]);
    internal static PassAccessDto Paid(Guid trip) => new(trip, TrialAccessState.Paid, StoreProvider.Google, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), null, 10);
    internal static PendingStorePurchase Pending(Guid owner, Guid trip) => new(owner, trip, Guid.NewGuid(), StoreProvider.Google, "test.pass", "opaque-test", StoreEnvironment.Sandbox, null, DateTimeOffset.UtcNow);

    internal sealed class Fixture : IAsyncDisposable
    {
        public AuthSessionService Session { get; } = new();
        public AuthSessionDto Initial { get; } = new(Guid.NewGuid(), "synthetic@example.test", "Synthetic", false, "owner-a-token", Guid.NewGuid()) { EmailVerified = true };
        public TravelCompanionApiClient Api { get; } = new();
        public ControlledPurchaseStore Store { get; } = new();
        public PendingStorePurchaseStore Pending { get; } = new();
        public PaywallViewModel ViewModel { get; }
        private readonly List<Guid> owners = [];
        public Fixture()
        {
            Api.FetchOffer = (trip, entry) => Task.FromResult<PaywallOfferDto?>(Offer(trip, entry));
            Api.CreateIntent = request => Task.FromResult<PurchaseIntentDto?>(new(Guid.NewGuid(), request.TripId, request.Provider, request.ProductId, "opaque-test", PurchaseIntentState.AwaitingConfirmation, DateTimeOffset.UtcNow, null));
            Api.VerifyPurchase = _ => Task.FromResult<PassAccessDto?>(Paid(Initial.TripId!.Value));
            Api.SelectTrip = _ => Task.FromResult<AuthSessionDto?>(Initial);
            ViewModel = new(Api, Session, new(), Store, new(), Pending, new());
            owners.Add(Initial.UserId);
        }
        public static async Task<Fixture> CreateAsync()
        {
            Shell.Current = new();
            var fixture = new Fixture(); await fixture.Session.SaveAsync(fixture.Initial); return fixture;
        }
        public async Task<AuthSessionDto> ChangeContextAsync(bool account)
        {
            var changed = Initial with { UserId = account ? Guid.NewGuid() : Initial.UserId,
                TripId = Guid.NewGuid(), Token = "new-context-token" };
            owners.Add(changed.UserId); await Session.SaveAsync(changed); return changed;
        }
        public async ValueTask DisposeAsync()
        {
            SecureStorage.Default.BeforeGet = null;
            SecureStorage.Default.BeforeSet = null;
            foreach (var owner in owners.Distinct())
            {
                while (await Pending.GetAsync(owner) is { } purchase) await Pending.ClearAsync(purchase);
                Session.DeleteUnlockPreference(owner);
            }
            Session.Clear(); Shell.Current = new();
        }
    }
}
