using System.Net;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class DocsViewModelTests
{
    [Fact]
    public async Task Successful_empty_response_shows_empty_only_after_loading_finishes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = new TaskCompletionSource<ApiCallResult<TravelDocsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.FetchDocuments = ct => response.Task.WaitAsync(ct);

        Assert.False(fixture.ViewModel.ShowEmptyState);
        var load = fixture.ViewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.IsInitialLoading);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        response.SetResult(ApiCallResult<TravelDocsDto>.Success(fixture.Docs()));
        await load;

        Assert.True(fixture.ViewModel.HasLoaded);
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.True(fixture.ViewModel.ShowEmptyState);
        Assert.False(fixture.ViewModel.HasError);
        Assert.Empty(fixture.ViewModel.DocumentGroups);
        Assert.Equal(1, fixture.Api.DocumentRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_without_cache_is_retryable_error_and_never_a_false_empty_state(bool throwsTransportError)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.FetchDocuments = _ => throwsTransportError
            ? throw new WebException("synthetic EOF", new IOException())
            : Task.FromResult(ApiCallResult<TravelDocsDto>.TransientFailure());

        await fixture.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.IsBusy);
        Assert.True(fixture.ViewModel.HasError);
        Assert.Equal(Text("DocsLoadFailed"), fixture.ViewModel.ErrorMessage);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        Assert.Empty(fixture.ViewModel.DocumentGroups);
        Assert.Equal(1, fixture.Api.DocumentRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Offline_without_downloaded_content_explains_missing_pass_content_and_keeps_personal_files(bool hasPersonalFile)
    {
        await using var fixture = await Fixture.CreateAsync();
        var personal = hasPersonalFile ? await fixture.AddPersonalAsync() : null;
        var writes = fixture.Cache.Writes;
        Connectivity.Current.NetworkAccess = NetworkAccess.None;

        await fixture.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.IsOffline);
        Assert.Equal(Text("DocsOfflineMissing"), fixture.ViewModel.ErrorMessage);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        if (personal is not null) Assert.Equal(personal.Id, Assert.Single(await fixture.Documents.ListAsync()).Id);
        Assert.Equal(0, fixture.Api.DocumentRequests);
        Assert.Equal(0, fixture.Sync.VersionRequests);
        Assert.Equal(writes, fixture.Cache.Writes);
    }

    [Fact]
    public async Task Offline_cache_and_personal_attachment_remain_available_without_fetching()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personal = await fixture.AddPersonalAsync();
        await fixture.SeedIncludedCacheAsync();
        Connectivity.Current.NetworkAccess = NetworkAccess.None;

        await fixture.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.HasDocuments);
        Assert.False(fixture.ViewModel.HasError);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        Assert.Equal(personal.Title, Assert.Single(fixture.PersonalRows()).Title);
        Assert.Equal("Included ticket", Assert.Single(fixture.IncludedRows()).Title);
        Assert.Equal(Text("RequiresConnection"), Assert.Single(fixture.IncludedRows()).Availability);
        Assert.Equal(0, fixture.Api.DocumentRequests);
        Assert.Equal(personal.Id, Assert.Single(await fixture.Documents.ListAsync()).Id);
    }

    [Fact]
    public async Task Failed_refresh_retains_both_sources_and_retry_can_clear_curated_rows_without_losing_personal_files()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personal = await fixture.AddPersonalAsync();
        await fixture.SeedIncludedCacheAsync();
        fixture.Api.FetchDocuments = _ => Task.FromResult(ApiCallResult<TravelDocsDto>.TransientFailure());

        await fixture.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(Text("DocsRefreshFailed"), fixture.ViewModel.ErrorMessage);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        Assert.Single(fixture.PersonalRows());
        Assert.Single(fixture.IncludedRows());
        Assert.Equal(1, fixture.Api.DocumentRequests);

        // An optional sync-state failure must not prevent the explicit document retry.
        fixture.Sync.Synchronize = _ => throw new HttpRequestException("synthetic sync-state failure");
        fixture.Api.FetchDocuments = _ => Task.FromResult(ApiCallResult<TravelDocsDto>.Success(fixture.Docs()));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.HasError);
        Assert.True(fixture.ViewModel.HasDocuments);
        Assert.False(fixture.ViewModel.ShowEmptyState);
        Assert.Empty(fixture.IncludedRows());
        Assert.Equal(personal.Title, Assert.Single(fixture.PersonalRows()).Title);
        Assert.Equal(personal.Id, Assert.Single(await fixture.Documents.ListAsync()).Id);
        Assert.Equal(2, fixture.Api.DocumentRequests);
        Assert.Equal(1, fixture.Sync.VersionRequests);
        Assert.Empty((await fixture.Cache.GetAsync<TravelDocsDto>(fixture.CacheKey))!.Value.OtherDocuments);
    }

    [Fact]
    public async Task Free_read_only_user_can_use_personal_documents_offline_without_expanding_included_access()
    {
        await using var fixture = await Fixture.CreateAsync(curated: false);
        var personal = await fixture.AddPersonalAsync();
        await fixture.SeedIncludedCacheAsync();
        Connectivity.Current.NetworkAccess = NetworkAccess.None;

        await fixture.ViewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(fixture.Sessions.HasCuratedDocs);
        Assert.False(fixture.Sessions.CanEditItinerary);
        Assert.True(fixture.ViewModel.CanAttachDocument);
        Assert.False(fixture.ViewModel.HasError);
        Assert.True(fixture.ViewModel.HasDocuments);
        Assert.Equal(personal.Title, Assert.Single(fixture.PersonalRows()).Title);
        Assert.Empty(fixture.IncludedRows());
        Assert.Equal(0, fixture.Api.DocumentRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Late_remote_response_after_account_or_trip_change_cannot_publish_or_cache_old_documents(bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = new TaskCompletionSource<ApiCallResult<TravelDocsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.FetchDocuments = ct => response.Task.WaitAsync(ct);
        var load = fixture.ViewModel.LoadCommand.ExecuteAsync(null);
        await fixture.Sessions.SaveAsync(fixture.Session with
        {
            UserId = changeAccount ? Guid.NewGuid() : fixture.Session.UserId,
            TripId = Guid.NewGuid()
        });
        response.SetResult(ApiCallResult<TravelDocsDto>.Success(fixture.Docs(included: true)));
        await load;

        Assert.Empty(fixture.ViewModel.DocumentGroups);
        Assert.Empty(fixture.ViewModel.OtherDocuments);
        Assert.Null(await fixture.Cache.GetAsync<TravelDocsDto>(fixture.CacheKey));
        Assert.Equal(0, fixture.Cache.Writes);
    }

    private static string Text(string key) => LocalizationResourceManager.Instance[key];

    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("cancel")]
    public async Task Leaving_the_document_context_during_category_selection_does_not_open_a_file_picker(string change)
    {
        await using var fixture = await Fixture.CreateAsync(curated: false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? category = null;
        Shell.Current.SelectAction = (_, buttons) =>
        {
            category = buttons[0];
            entered.TrySetResult();
            return release.Task;
        };
        var attach = fixture.ViewModel.AttachDocumentCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "cancel") fixture.ViewModel.CancelLoading();
            else await fixture.Sessions.SaveAsync(fixture.Session with
            {
                UserId = change == "account" ? Guid.NewGuid() : fixture.Session.UserId,
                TripId = Guid.NewGuid()
            });
            release.TrySetResult(category!);
            await attach.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, fixture.Picker.Calls);
            Assert.Equal(0, fixture.Cache.Writes);
            Assert.False(fixture.ViewModel.HasError);
            Assert.Null(fixture.ViewModel.StatusMessage);
        }
        finally { release.TrySetResult(category ?? "Cancel"); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refresh_cannot_start_sync_with_a_token_from_a_previous_account_or_trip(bool changeAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SecureStorage.Default.BeforeGet = key =>
        {
            if (!key.StartsWith("auth_token", StringComparison.Ordinal)) return Task.CompletedTask;
            entered.TrySetResult();
            return release.Task;
        };
        try
        {
            var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Sessions.SaveAsync(fixture.Session with
            {
                UserId = changeAccount ? Guid.NewGuid() : fixture.Session.UserId,
                TripId = Guid.NewGuid()
            });
            release.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, fixture.Sync.VersionRequests);
            Assert.Equal(0, fixture.Api.DocumentRequests);
            Assert.Equal(0, fixture.Cache.Writes);
        }
        finally
        {
            SecureStorage.Default.BeforeGet = null;
            release.TrySetResult();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AuthSessionService Sessions { get; } = new();
        public AuthSessionDto Session { get; private init; } = null!;
        public TravelCompanionApiClient Api { get; } = new();
        public OfflineCacheService Cache { get; } = new();
        public OfflineSyncCoordinator Sync { get; } = new();
        public TripDocumentStore Documents { get; private set; } = null!;
        public DocsViewModel ViewModel { get; private set; } = null!;
        public CancelPicker Picker { get; } = new();
        public string CacheKey => $"mobile-docs-{Session.TripId}-{Session.UserId}";

        public static async Task<Fixture> CreateAsync(bool curated = true)
        {
            var fixture = new Fixture
            {
                Session = new(Guid.NewGuid(), "documents@example.invalid", "Review", false, "token", Guid.NewGuid(),
                    AccessMode: curated ? SessionAccessMode.Trip : SessionAccessMode.BuilderReadOnly,
                    ExperienceMode: curated ? ExperienceMode.CuratedPremium : ExperienceMode.SelfServiceBuilder,
                    Capabilities: new(true, false, false, curated, false))
            };
            Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            Shell.Current = new();
            await fixture.Sessions.SaveAsync(fixture.Session);
            fixture.Documents = new(fixture.Cache, fixture.Sessions, fixture.Api, new ReservationDocumentLinkStore(fixture.Cache, fixture.Sessions));
            var organizer = new TripPreparationOrganizerStore(fixture.Cache, fixture.Sessions);
            var attachments = new TripDocumentAttachmentService(fixture.Documents, organizer, fixture.Sessions, fixture.Picker);
            fixture.ViewModel = new(fixture.Api, fixture.Sessions, fixture.Cache, fixture.Sync,
                new MobileSyncStateStore(), fixture.Documents, attachments);
            return fixture;
        }

        public async Task<LocalTripDocument> AddPersonalAsync()
        {
            using var file = new MemoryStream("%PDF-1.7 synthetic personal attachment"u8.ToArray());
            return await Documents.AttachAsync(file, "My hotel.pdf", LocalDocumentCategory.Accommodation);
        }

        public TravelDocsDto Docs(bool included = false) => new(Session.TripId!.Value, "Review", "Japan",
            new(2026, 10, 7), new(2026, 10, 14), null, [],
            included ? [new(Guid.NewGuid(), TravelDocumentCategory.Other, "Included ticket", "Review", "/review.pdf", 0)] : [], []);

        public Task SeedIncludedCacheAsync() => Cache.SaveAsync(CacheKey, Docs(included: true));
        public IEnumerable<LocalDocumentItemViewModel> PersonalRows() => ViewModel.DocumentGroups.SelectMany(group => group).OfType<LocalDocumentItemViewModel>();
        public IEnumerable<DocumentItemViewModel> IncludedRows() => ViewModel.DocumentGroups.SelectMany(group => group).OfType<DocumentItemViewModel>();

        public ValueTask DisposeAsync()
        {
            ViewModel.CancelLoading();
            Sessions.Clear();
            Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            Shell.Current = new();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelPicker : ITripDocumentPicker
    {
        public int Calls { get; private set; }
        public Task<PickedTripDocument?> PickAsync(string title)
        { Calls++; return Task.FromResult<PickedTripDocument?>(null); }
    }
}
