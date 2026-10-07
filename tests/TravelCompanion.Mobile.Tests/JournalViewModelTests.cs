using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class JournalViewModelTests
{
    [Theory]
    [InlineData("account")]
    [InlineData("trip")]
    [InlineData("page")]
    public async Task LateTokenCannotStartBootstrapRequestAfterContextChanges(string change)
    {
        var sessions = new AuthSessionService(); var account = Session(); var bootstrap = new MobileBootstrapStore();
        var store = new JournalStore(new(), sessions, new()); var viewModel = new JournalViewModel(bootstrap, sessions, store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await sessions.SaveAsync(account); Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
            SecureStorage.Default.BeforeGet = async key => { if (key.StartsWith("auth_token", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; } };
            var refresh = viewModel.RefreshCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "page") viewModel.CancelLoading();
            else await sessions.SaveAsync(change == "account" ? Session() : account with { TripId = Guid.NewGuid() });
            release.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, bootstrap.RefreshRequests);
        }
        finally
        {
            release.TrySetResult(); SecureStorage.Default.BeforeGet = null;
            viewModel.CancelLoading(); sessions.Clear(); Connectivity.Current.NetworkAccess = NetworkAccess.Internet;
        }
    }

    [Fact]
    public async Task LargeJournalLoadsVisibleThumbnailsAndRetainsRowsAndViewportAfterRefresh()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService(); var bootstrap = new MobileBootstrapStore();
        var store = new JournalStore(disk, sessions, new()); var viewModel = new JournalViewModel(bootstrap, sessions, store);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); Connectivity.Current.NetworkAccess = NetworkAccess.None;
            for (var i = 0; i < 250; i++)
            {
                var id = Guid.NewGuid();
                var memory = JournalMemory.NewFree(scope.TripId, new DateOnly(2026, 1, 1).AddDays(i)) with { Photos = [new(id)], CoverId = id };
                await store.SaveAsync(scope, memory, $"Memory {i}");
                await disk.SaveAsync($"personal-journal-{scope.UserId}-{scope.TripId}-thumbnail-{id}", new JournalPhotoPayload([1, 2, 3]));
            }
            await viewModel.LoadAsync();
            Assert.Equal(250, viewModel.Entries.Count);
            Assert.Equal(6, viewModel.Entries.SelectMany(row => row.Photos).Count(photo => photo.Source.OpenStream is not null));
            var rows = viewModel.Entries.ToArray();
            var reads = disk.Reads;
            viewModel.SetVisibleRange(100, 103);
            Assert.Equal(6, disk.Reads - reads);
            Assert.All(viewModel.Entries.Skip(99).Take(6), row => Assert.NotNull(Assert.Single(row.Photos).Source.OpenStream));
            reads = disk.Reads;
            var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
            viewModel.Entries.CollectionChanged += (_, args) => changes.Add(args.Action);
            await viewModel.LoadAsync();
            Assert.Equal(rows.Length, viewModel.Entries.Count);
            for (var i = 0; i < rows.Length; i++) Assert.Same(rows[i], viewModel.Entries[i]);
            Assert.Empty(changes);
            // Refresh reads the index and drafts; the remembered viewport uses its cached thumbnails.
            Assert.Equal(2, disk.Reads - reads);
            Assert.All(viewModel.Entries.Skip(99).Take(6), row => Assert.NotNull(Assert.Single(row.Photos).Source.OpenStream));
        }
        finally { viewModel.CancelLoading(); sessions.Clear(); Connectivity.Current.NetworkAccess = NetworkAccess.Internet; }
    }

    [Fact]
    public async Task MissingThumbnailKeepsPlaceholderAndDoesNotStopLaterVisiblePhotos()
    {
        var sessions = new AuthSessionService(); var disk = new OfflineCacheService();
        var store = new JournalStore(disk, sessions, new()); var viewModel = new JournalViewModel(new(), sessions, store);
        try
        {
            await sessions.SaveAsync(Session()); var scope = store.Scope(); Connectivity.Current.NetworkAccess = NetworkAccess.None;
            var absentPhoto = Guid.NewGuid(); var availablePhoto = Guid.NewGuid();
            var missing = JournalMemory.NewFree(scope.TripId, new(2026, 10, 1)) with { Photos = [new(absentPhoto)], CoverId = absentPhoto };
            var available = JournalMemory.NewFree(scope.TripId, new(2026, 10, 2)) with { Photos = [new(availablePhoto)], CoverId = availablePhoto };
            await store.SaveAsync(scope, missing, "Original missing on this device");
            await store.SaveAsync(scope, available, "Available photo");
            await disk.SaveAsync($"personal-journal-{scope.UserId}-{scope.TripId}-thumbnail-{availablePhoto}", new JournalPhotoPayload([1, 2, 3]));

            await viewModel.LoadAsync();

            Assert.Equal(2, viewModel.Entries.Count);
            var placeholder = Assert.Single(viewModel.Entries[0].Photos).Source;
            Assert.Equal("journal_photo.svg", placeholder.File); Assert.Null(placeholder.OpenStream);
            var loaded = Assert.Single(viewModel.Entries[1].Photos).Source;
            using var stream = loaded.OpenStream!();
            Assert.Equal([1, 2, 3], ((MemoryStream)stream).ToArray());
            Assert.Equal(absentPhoto, viewModel.Entries[0].Memory.CoverId);
            Assert.Equal(availablePhoto, viewModel.Entries[1].Memory.CoverId);
            Assert.Null(viewModel.ErrorMessage);
        }
        finally { viewModel.CancelLoading(); sessions.Clear(); Connectivity.Current.NetworkAccess = NetworkAccess.Internet; }
    }

    private static AuthSessionDto Session() => new(Guid.NewGuid(), "journal-vm@example.test", "Test", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.FreeMapPreview, ExperienceMode: ExperienceMode.FreePreview, Capabilities: new(false, false, false, false, false));
}
