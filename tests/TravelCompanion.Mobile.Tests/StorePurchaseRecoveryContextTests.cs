using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;
using static TravelCompanion.Mobile.Tests.PaywallViewModelContextTests;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class StorePurchaseRecoveryContextTests
{
    [Theory]
    [InlineData("restore_passes")] [InlineData("verify")] [InlineData("finish")] [InlineData("select")]
    public async Task Recovery_never_activates_an_old_owner_after_account_changes(string stage)
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = Pending(fixture.Initial.UserId, fixture.Initial.TripId!.Value) with { Evidence = "synthetic-receipt" };
        await fixture.Pending.SaveAsync(pending);
        var entered = Signal(); var release = Signal();
        if (stage == "restore_passes") fixture.Api.RestorePasses = async () => { entered.TrySetResult(); await release.Task; return []; };
        if (stage == "verify") fixture.Api.VerifyPurchase = async _ => { entered.TrySetResult(); await release.Task; return Paid(pending.TripId); };
        if (stage == "finish") fixture.Store.Finish = async () => { entered.TrySetResult(); await release.Task; };
        if (stage == "select") fixture.Api.SelectTrip = async _ => { entered.TrySetResult(); await release.Task; return fixture.Initial; };
        var sync = new OfflineSyncCoordinator();
        var service = new StorePurchaseRecoveryService(fixture.Session, fixture.Api, fixture.Store, fixture.Pending, sync);
        var recovery = service.RecoverAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await fixture.ChangeContextAsync(true);
        release.TrySetResult(); await recovery;
        Assert.Equal(current.UserId, fixture.Session.CurrentUserId);
        Assert.Equal(current.TripId, fixture.Session.CurrentTripId);
        Assert.Equal(current.Token, await fixture.Session.GetTokenAsync());
        Assert.Equal(pending, await fixture.Pending.GetAsync(pending.UserId));
        Assert.Equal(0, sync.SynchronizeTriggers);
        Assert.Equal(stage == "restore_passes" ? 0 : 1, fixture.Api.VerificationRequests);
        Assert.Equal(stage == "finish" || stage == "select" ? 1 : 0, fixture.Store.FinishCalls);
        Assert.Equal(stage == "select" ? 1 : 0, fixture.Api.SelectTripRequests);
    }

    [Fact]
    public async Task Restored_native_evidence_is_kept_for_old_owner_even_when_account_changes_during_restore()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = Pending(fixture.Initial.UserId, fixture.Initial.TripId!.Value);
        await fixture.Pending.SaveAsync(pending);
        var entered = Signal(); var release = Signal();
        fixture.Store.Restore = async () => { entered.TrySetResult(); await release.Task; return ["synthetic-restored-evidence"]; };
        var service = new StorePurchaseRecoveryService(fixture.Session, fixture.Api, fixture.Store, fixture.Pending, new());
        var recovery = service.RecoverAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await fixture.ChangeContextAsync(true);
        var newPending = Pending(current.UserId, current.TripId!.Value);
        await fixture.Pending.SaveAsync(newPending);
        release.TrySetResult(); await recovery;
        Assert.Equal("synthetic-restored-evidence", (await fixture.Pending.GetAsync(pending.UserId))!.Evidence);
        Assert.Equal(newPending, await fixture.Pending.GetAsync(current.UserId));
        Assert.Equal(0, fixture.Api.VerificationRequests);
        Assert.Equal(0, fixture.Api.SelectTripRequests);
        Assert.Equal(current.UserId, fixture.Session.CurrentUserId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recovery_completes_paid_or_verified_pending_and_retains_other_accounts_receipts(bool alreadyPaid)
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = Pending(fixture.Initial.UserId, fixture.Initial.TripId!.Value) with { Evidence = "synthetic-receipt" };
        var other = Pending(Guid.NewGuid(), Guid.NewGuid());
        await fixture.Pending.SaveAsync(pending); await fixture.Pending.SaveAsync(other);
        if (alreadyPaid) fixture.Api.RestorePasses = () => Task.FromResult<IReadOnlyList<PassAccessDto>>([Paid(pending.TripId)]);
        var sync = new OfflineSyncCoordinator();
        var service = new StorePurchaseRecoveryService(fixture.Session, fixture.Api, fixture.Store, fixture.Pending, sync);
        await service.RecoverAsync();
        Assert.Null(await fixture.Pending.GetAsync(pending.UserId));
        Assert.Equal(other, await fixture.Pending.GetAsync(other.UserId));
        Assert.Equal(alreadyPaid ? 0 : 1, fixture.Api.VerificationRequests);
        Assert.Equal(1, fixture.Store.FinishCalls);
        Assert.Equal(1, fixture.Api.SelectTripRequests);
        Assert.Equal(1, sync.SynchronizeTriggers);
        await fixture.Pending.ClearAsync(other);
    }
}
