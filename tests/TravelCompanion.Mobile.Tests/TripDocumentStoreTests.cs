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
        var store = CreateStore(new OfflineCacheService(), sessions);
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
        var store = CreateStore(disk, sessions);
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
    public async Task Document_links_are_scoped_and_deletion_clears_them_without_touching_other_files()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var disk = new OfflineCacheService();
        var links = new ReservationDocumentLinkStore(disk, sessions);
        var store = new TripDocumentStore(disk, sessions, new TravelCompanionApiClient(), links);
        var reservationId = Guid.NewGuid();
        try
        {
            await sessions.SaveAsync(account);
            await store.AttachAsync(new MemoryStream("%PDF-1.7 ticket"u8.ToArray()), "ticket.pdf");
            await store.AttachAsync(new MemoryStream("%PDF-1.7 hotel"u8.ToArray()), "hotel.pdf");
            var files = await store.ListAsync();
            var ticket = files.Single(file => file.Title == "ticket");
            Assert.True(await store.ExistsAsync(ticket.Id));
            await links.SetAsync(new ReservationDocumentLink(reservationId, ticket.Id, null, ticket.Title));
            Assert.Equal(ticket.Id, (await links.GetAsync(reservationId))?.LocalDocumentId);

            await sessions.SaveAsync(Session());
            Assert.Null(await links.GetAsync(reservationId));
            await sessions.SaveAsync(account);
            await links.RemoveReservationAsync(reservationId);
            Assert.Null(await links.GetAsync(reservationId));
            Assert.True(await store.ExistsAsync(ticket.Id));
            await links.SetAsync(new ReservationDocumentLink(reservationId, ticket.Id, null, ticket.Title));
            await store.DeleteAsync(ticket.Id);
            Assert.False(await store.ExistsAsync(ticket.Id));
            Assert.Null(await links.GetAsync(reservationId));
            Assert.Equal("hotel", Assert.Single(await store.ListAsync()).Title);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Read_only_or_free_access_can_attach_personal_documents()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = CreateStore(new OfflineCacheService(), sessions);
        try
        {
            await sessions.SaveAsync(account);
            await store.AttachAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), "ticket.pdf");
            await sessions.SaveAsync(account with { AccessMode = SessionAccessMode.BuilderReadOnly, Capabilities = new(true, false, false, false, false) });
            Assert.Single(await store.ListAsync());
            Assert.True(store.CanAttach);
            await store.AttachAsync(new MemoryStream("%PDF-1.7 second"u8.ToArray()), "new.pdf");
            Assert.Equal(2, (await store.ListAsync()).Count);
        }
        finally { sessions.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downloaded_curated_document_requires_current_access_but_personal_document_remains_free(bool curated)
    {
        var sessions = new AuthSessionService();
        var account = Session() with { AccessMode = SessionAccessMode.FreeMapPreview, Capabilities = new(false, false, false, false, false) };
        var disk = new OfflineCacheService(); var store = CreateStore(disk, sessions);
        try
        {
            await sessions.SaveAsync(account);
            var document = await store.AttachAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), "ticket.pdf");
            if (curated) MarkAsCurated(disk, account, document);
            var opens = Launcher.OpenFileCalls;
            if (curated)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.OpenAsync(document.Id));
                Assert.Equal(opens, Launcher.OpenFileCalls);
            }
            else
            {
                await store.OpenAsync(document.Id);
                Assert.Equal(opens + 1, Launcher.OpenFileCalls);
            }
        }
        finally { sessions.Clear(); await TripDocumentStore.ClearPreviewsAsync(); }
    }

    [Fact]
    public async Task Downloaded_curated_document_can_open_with_current_permission()
    {
        var sessions = new AuthSessionService(); var account = Session() with { Capabilities = new(true, true, true, true, false) };
        var disk = new OfflineCacheService(); var store = CreateStore(disk, sessions);
        try
        {
            await sessions.SaveAsync(account);
            var document = await store.AttachAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), "ticket.pdf");
            MarkAsCurated(disk, account, document);
            var opens = Launcher.OpenFileCalls;
            await store.OpenAsync(document.Id);
            Assert.Equal(opens + 1, Launcher.OpenFileCalls);
        }
        finally { sessions.Clear(); await TripDocumentStore.ClearPreviewsAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoking_curated_permission_or_expiring_access_during_payload_read_does_not_open_or_leave_a_preview(bool expireAccess)
    {
        var sessions = new AuthSessionService(); var account = Session() with { Capabilities = new(true, true, true, true, false) };
        var disk = new OfflineCacheService(); var store = CreateStore(disk, sessions);
        try
        {
            await sessions.SaveAsync(account);
            var document = await store.AttachAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), "ticket.pdf");
            MarkAsCurated(disk, account, document);
            var reads = 0;
            disk.BeforeRead = () =>
            {
                if (++reads == 2)
                {
                    if (expireAccess) sessions.ApplySyncState(new(account.Capabilities!, DateTimeOffset.UtcNow.AddMinutes(-1),
                        account.TripId, null, null, 0, 0, 0, 0, 0));
                    else sessions.ApplyCapabilities(new(true, true, true, false, false));
                }
                return Task.CompletedTask;
            };
            var opens = Launcher.OpenFileCalls;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.OpenAsync(document.Id));
            Assert.Equal(2, reads);
            Assert.Equal(opens, Launcher.OpenFileCalls);
            Assert.False(File.Exists(Path.Combine(FileSystem.CacheDirectory, "document-preview", document.Id.ToString("N") + ".pdf")));
        }
        finally { sessions.Clear(); await TripDocumentStore.ClearPreviewsAsync(); }
    }

    private static void MarkAsCurated(OfflineCacheService disk, AuthSessionDto account, LocalTripDocument document) =>
        disk.Entries[$"personal-documents-{account.UserId:N}-{account.TripId:N}"] =
            new OfflineCacheResult<List<LocalTripDocument>>([document with { SourceUrl = "/trip-files/ticket.pdf" }], DateTimeOffset.UtcNow);

    [Fact]
    public async Task Deletion_during_a_read_cannot_recreate_the_deleted_trip_files()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = CreateStore(new OfflineCacheService(), sessions);
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
    public async Task Access_expiry_during_file_read_does_not_block_personal_attachment()
    {
        var sessions = new AuthSessionService();
        var account = Session();
        var store = CreateStore(new OfflineCacheService(), sessions);
        try
        {
            await sessions.SaveAsync(account);
            using var stream = new SwitchingStream(() => sessions.SaveAsync(account with
            {
                AccessMode = SessionAccessMode.BuilderReadOnly,
                Capabilities = new(true, false, false, false, false)
            }));
            await store.AttachAsync(stream, "ticket.pdf");
            Assert.Single(await store.ListAsync());
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
        var store = CreateStore(disk, sessions);
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

    private static TripDocumentStore CreateStore(OfflineCacheService disk, AuthSessionService sessions) =>
        new(disk, sessions, new TravelCompanionApiClient(), new ReservationDocumentLinkStore(disk, sessions));
}
