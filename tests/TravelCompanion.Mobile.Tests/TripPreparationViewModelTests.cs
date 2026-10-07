using System.Net;
using System.Text;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class TripPreparationViewModelTests
{
    [Fact]
    public async Task Pending_optional_analytics_does_not_hold_loading_or_local_mutations()
    {
        await using var fixture = await Fixture.CreateAsync();
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        var analytics = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Analytics.Track = () => analytics.Task;
        try
        {
            await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.ViewModel.AttachItemCommand.ExecuteAsync(fixture.ViewModel.Items[0]).WaitAsync(TimeSpan.FromSeconds(5));
            Shell.Current.SelectAction = (_, choices) => Task.FromResult(choices[1]);
            await fixture.ViewModel.MoreItemCommand.ExecuteAsync(fixture.ViewModel.Items[1]).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(fixture.ViewModel.IsBusy);
            Assert.True(fixture.ViewModel.CanSave);
            Assert.Equal(1, fixture.ViewModel.Items[0].DocumentCount);
            Assert.Equal(PreparationManualState.NotNeeded, fixture.ViewModel.Items[1].State.ManualState);
            Assert.False(analytics.Task.IsCompleted);
        }
        finally { analytics.TrySetResult(); }
    }

    [Fact]
    public async Task Slow_legacy_request_does_not_block_local_attachments_or_manual_decisions_or_repeat_after_them()
    {
        await using var fixture = await Fixture.CreateAsync();
        var remote = new TaskCompletionSource<List<TripPreparationItemDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.FetchPreparation = ct => remote.Task.WaitAsync(ct);
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.IsBusy);
        Assert.True(fixture.ViewModel.CanSave);
        Assert.Equal(4, fixture.ViewModel.Items.Count);
        Assert.Equal(1, fixture.Api.PreparationRequests);
        await fixture.ViewModel.AttachItemCommand.ExecuteAsync(fixture.ViewModel.Items.Single(row => row.Key == "accommodation"));
        Assert.Equal(1, fixture.ViewModel.Items.Single(row => row.Key == "accommodation").DocumentCount);
        Shell.Current.SelectAction = (_, choices) => Task.FromResult(choices[1]);
        await fixture.ViewModel.MoreItemCommand.ExecuteAsync(fixture.ViewModel.Items.Single(row => row.Key == "transport"));
        Assert.Equal(PreparationManualState.NotNeeded, (await fixture.Organizer.GetAsync()).Categories.Single(row => row.Key == "transport").ManualState);
        Assert.Equal(1, fixture.Api.PreparationRequests);

        remote.SetResult([new("transport", true, 1), new("accommodation", true, 1)]);
        await fixture.ViewModel.LegacyImportCompletion;

        Assert.Equal(PreparationManualState.NotNeeded, fixture.ViewModel.Items.Single(row => row.Key == "transport").State.ManualState);
        Assert.Equal(PreparationManualState.Pending, fixture.ViewModel.Items.Single(row => row.Key == "accommodation").State.ManualState);
        Assert.Equal(1, fixture.ViewModel.Items.Single(row => row.Key == "accommodation").DocumentCount);
        Assert.True((await fixture.Organizer.GetAsync()).LegacyImported);
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Api.PreparationRequests);
    }

    [Fact]
    public async Task Completed_import_and_offline_use_never_need_the_legacy_endpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Organizer.ImportLegacyOnceAsync([new("transport", true, 1)]);
        fixture.Api.FetchPreparation = _ => throw new InvalidOperationException("Must not fetch again");
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.Items.Single(row => row.Key == "transport").IsOrganized);
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.CanSave);
        Assert.Equal(0, fixture.Api.PreparationRequests);
    }

    [Fact]
    public async Task Legacy_transport_failure_preserves_local_actions_and_has_separate_recoverable_notice()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.FetchPreparation = _ => throw new WebException("Connection dropped", new IOException("EOF"));
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        await fixture.ViewModel.LegacyImportCompletion;

        Assert.True(fixture.ViewModel.CanSave);
        Assert.False(fixture.ViewModel.HasError);
        Assert.True(fixture.ViewModel.HasImportNotice);
        await fixture.ViewModel.AttachItemCommand.ExecuteAsync(fixture.ViewModel.Items[0]);
        Assert.Single(await fixture.Documents.ListAsync());
        Assert.Equal(1, fixture.Api.PreparationRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Account_or_trip_change_during_import_cannot_write_legacy_state_into_the_new_context(bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var remote = new TaskCompletionSource<List<TripPreparationItemDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.FetchPreparation = ct => remote.Task.WaitAsync(ct);
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        await fixture.Sessions.SaveAsync(changeAccount ? Session() : fixture.Session with { TripId = Guid.NewGuid() });
        remote.SetResult([new("transport", true, 1)]);
        await fixture.ViewModel.LegacyImportCompletion;

        var next = await fixture.Organizer.GetAsync();
        Assert.False(next.LegacyImported);
        Assert.All(next.Categories, row => Assert.Equal(PreparationManualState.Pending, row.ManualState));
    }

    [Fact]
    public async Task Account_change_in_manual_action_sheet_discards_the_selected_decision()
    {
        await using var fixture = await Fixture.CreateAsync();
        Connectivity.Current.NetworkAccess = NetworkAccess.None;
        await fixture.ViewModel.LoadPreparationCommand.ExecuteAsync(null);
        Shell.Current.SelectAction = async (_, choices) => { await fixture.Sessions.SaveAsync(Session()); return choices[0]; };
        await fixture.ViewModel.MoreItemCommand.ExecuteAsync(fixture.ViewModel.Items[0]);
        Assert.All((await fixture.Organizer.GetAsync()).Categories, row => Assert.Equal(PreparationManualState.Pending, row.ManualState));
    }

    private static AuthSessionDto Session() => new(Guid.NewGuid(), "review@example.invalid", "Review", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.BuilderReadOnly, ExperienceMode: ExperienceMode.SelfServiceBuilder,
        Capabilities: new(true, false, false, false, false));

    private sealed class Fixture : IAsyncDisposable
    {
        public AuthSessionService Sessions { get; } = new();
        public AuthSessionDto Session { get; } = TripPreparationViewModelTests.Session();
        public TravelCompanionApiClient Api { get; } = new();
        public ProductAnalyticsTracker Analytics { get; } = new();
        public TripDocumentStore Documents { get; private set; } = null!;
        public TripPreparationOrganizerStore Organizer { get; private set; } = null!;
        public TripPreparationViewModel ViewModel { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            Shell.Current = new();
            await fixture.Sessions.SaveAsync(fixture.Session);
            var cache = new OfflineCacheService();
            fixture.Organizer = new(cache, fixture.Sessions);
            fixture.Documents = new(cache, fixture.Sessions, fixture.Api, new ReservationDocumentLinkStore(cache, fixture.Sessions));
            var attachments = new TripDocumentAttachmentService(fixture.Documents, fixture.Organizer, fixture.Sessions, new Picker());
            fixture.ViewModel = new(fixture.Sessions, fixture.Api, new MobileBootstrapStore(), cache,
                new OfflineTripPreparationService(), new MobileSyncStateStore(), fixture.Documents, fixture.Organizer, attachments, fixture.Analytics);
            return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            ViewModel.CancelLegacyImport();
            await ViewModel.LegacyImportCompletion;
            Sessions.Clear();
            Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            Shell.Current = new();
        }
    }
    private sealed class Picker : ITripDocumentPicker
    {
        public Task<PickedTripDocument?> PickAsync(string title) => Task.FromResult<PickedTripDocument?>(new("review.pdf",
            () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 synthetic review")))));
    }
}
