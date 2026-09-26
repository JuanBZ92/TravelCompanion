using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class TripDocumentStoreTests
{
    [Fact]
    public async Task Deleting_trip_then_account_removes_only_the_requested_owners_files()
    {
        var sessions = new AuthSessionService();
        var first = Session();
        var anotherTrip = first with { TripId = Guid.NewGuid() };
        var otherUser = Session();
        var store = new TripDocumentStore(new OfflineCacheService(), sessions, new TravelCompanionApiClient());
        try
        {
            foreach (var account in new[] { first, anotherTrip, otherUser })
            {
                await sessions.SaveAsync(account);
                using var file = new MemoryStream("%PDF-1.7 test"u8.ToArray());
                await store.AttachAsync(file, "ticket.pdf");
            }
            await store.DeleteTripAsync(first.UserId, first.TripId!.Value);
            await sessions.SaveAsync(first);
            Assert.Empty(await store.ListAsync());
            await sessions.SaveAsync(anotherTrip);
            Assert.Single(await store.ListAsync());
            await store.DeleteAccountAsync(first.UserId);
            Assert.Empty(await store.ListAsync());
            await sessions.SaveAsync(otherUser);
            Assert.Single(await store.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.Builder, ExperienceMode: ExperienceMode.SelfServiceBuilder,
        Capabilities: new(true, true, true, false, false));

    [Fact]
    public async Task Attachments_survive_logout_but_are_isolated_by_account_and_trip()
    {
        var sessions = new AuthSessionService();
        var first = Session();
        var disk = new OfflineCacheService();
        var store = new TripDocumentStore(disk, sessions, new TravelCompanionApiClient());
        try
        {
            await sessions.SaveAsync(first);
            using var source = new MemoryStream("%PDF-1.7 test"u8.ToArray());
            await store.AttachAsync(source, "boarding.pdf");
            var file = Assert.Single(await store.ListAsync());
            Assert.Equal("boarding", file.Title);
            await store.RenameAsync(file.Id, "Flight ticket");
            sessions.Clear();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ListAsync());
            await sessions.SaveAsync(Session());
            Assert.Empty(await store.ListAsync());
            await sessions.SaveAsync(first with { TripId = Guid.NewGuid() });
            Assert.Empty(await store.ListAsync());
            await sessions.SaveAsync(first);
            Assert.Equal("Flight ticket", Assert.Single(await store.ListAsync()).Title);
            await store.DeleteAsync(file.Id);
            Assert.Empty(await store.ListAsync());
            Assert.True(source.CanRead);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Expired_pass_can_read_saved_files_but_cannot_attach()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = new TripDocumentStore(new OfflineCacheService(), sessions, new TravelCompanionApiClient());
        try
        {
            await sessions.SaveAsync(account);
            await store.AttachAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), "ticket.pdf");
            await sessions.SaveAsync(account with { AccessMode = SessionAccessMode.BuilderReadOnly, Capabilities = new(true, false, false, false, false) });
            Assert.Single(await store.ListAsync());
            Assert.False(store.CanAttach);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.AttachAsync(new MemoryStream([]), "new.pdf"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Deletion_during_a_read_cannot_recreate_the_deleted_trip_files()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = new TripDocumentStore(new OfflineCacheService(), sessions, new TravelCompanionApiClient());
        try
        {
            await sessions.SaveAsync(account);
            using var stream = new SwitchingStream(() => store.DeleteTripAsync(account.UserId, account.TripId!.Value));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.AttachAsync(stream, "ticket.pdf"));
            Assert.Empty(await store.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Expiring_access_during_file_selection_does_not_save_the_attachment()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = new TripDocumentStore(new OfflineCacheService(), sessions, new TravelCompanionApiClient());
        try
        {
            await sessions.SaveAsync(account);
            using var stream = new SwitchingStream(() => sessions.SaveAsync(account with
            {
                AccessMode = SessionAccessMode.BuilderReadOnly,
                Capabilities = new(true, false, false, false, false)
            }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.AttachAsync(stream, "ticket.pdf"));
            Assert.Empty(await store.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Switching_accounts_during_a_file_read_does_not_save_into_either_account()
    {
        var sessions = new AuthSessionService();
        var first = Session();
        var second = Session();
        var disk = new OfflineCacheService();
        var store = new TripDocumentStore(disk, sessions, new TravelCompanionApiClient());
        try
        {
            await sessions.SaveAsync(first);
            using var stream = new SwitchingStream(() => sessions.SaveAsync(second));
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.AttachAsync(stream, "ticket.pdf"));
            Assert.Empty(await store.ListAsync());
            await sessions.SaveAsync(first);
            Assert.Empty(await store.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    private sealed class SwitchingStream(Func<Task> switchAccount) : MemoryStream("%PDF-1.7 test"u8.ToArray())
    {
        private bool _switched;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_switched) { _switched = true; await switchAccount(); }
            return await base.ReadAsync(buffer, ct);
        }
    }
}
