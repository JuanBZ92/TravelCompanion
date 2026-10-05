using System.Text.Json;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Free map session")]
public sealed class TripPreparationOrganizerTests
{
    [Fact]
    public async Task Legacy_checks_import_once_without_overwriting_new_manual_state()
    {
        var sessions = new AuthSessionService();
        var organizer = new TripPreparationOrganizerStore(new OfflineCacheService(), sessions);
        try
        {
            await sessions.SaveAsync(Session());
            await organizer.SetManualStateAsync("accommodation", PreparationManualState.NotNeeded);
            await organizer.ImportLegacyOnceAsync([
                new("transport", true, 1), new("accommodation", true, 1), new("reservations", false, 0)]);

            var imported = await organizer.GetAsync();
            Assert.Equal(PreparationManualState.OutsideApp, State(imported, "transport"));
            Assert.Equal(PreparationManualState.NotNeeded, State(imported, "accommodation"));

            await organizer.SetManualStateAsync("transport", PreparationManualState.Pending);
            await organizer.ImportLegacyOnceAsync([new("transport", true, 2)]);
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "transport"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Organizer_state_is_isolated_by_account_and_trip_and_available_offline()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        var first = Session();
        var second = Session();
        try
        {
            await sessions.SaveAsync(first);
            await organizer.SetManualStateAsync("transport", PreparationManualState.OutsideApp);
            await sessions.SaveAsync(second);
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "transport"));
            await sessions.SaveAsync(first with { TripId = Guid.NewGuid() });
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "transport"));
            await sessions.SaveAsync(first);
            Assert.Equal(PreparationManualState.OutsideApp, State(await organizer.GetAsync(), "transport"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Deleting_a_trip_removes_its_organizer_metadata()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var documents = CreateDocuments(disk, sessions);
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        var account = Session();
        try
        {
            await sessions.SaveAsync(account);
            await organizer.SetManualStateAsync("travel-documents", PreparationManualState.OutsideApp);
            await documents.DeleteTripAsync(account.UserId, account.TripId!.Value);
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "travel-documents"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Attaching_clears_previous_declaration_and_deleting_last_file_keeps_only_later_declarations()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var documents = CreateDocuments(disk, sessions);
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        try
        {
            await sessions.SaveAsync(Session());
            await organizer.SetManualStateAsync("transport", PreparationManualState.NotNeeded);
            var attachment = new TripDocumentAttachmentService(documents, organizer, sessions,
                Picker("ticket.pdf", "%PDF-1.7 ticket"));
            var saved = await attachment.PickAndAttachAsync(LocalDocumentCategory.Transport, "transport", "Add");

            Assert.Equal(LocalDocumentCategory.Transport, saved?.Category);
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "transport"));
            await documents.DeleteAsync(saved!.Id);
            Assert.Equal(PreparationManualState.Pending, State(await organizer.GetAsync(), "transport"));

            saved = await attachment.PickAndAttachAsync(LocalDocumentCategory.Transport, "transport", "Add");
            await organizer.SetManualStateAsync("transport", PreparationManualState.OutsideApp);
            await documents.DeleteAsync(saved!.Id);
            Assert.Empty(await documents.ListAsync());
            Assert.Equal(PreparationManualState.OutsideApp, State(await organizer.GetAsync(), "transport"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Personal_attachment_access_does_not_grant_curated_document_downloads()
    {
        var sessions = new AuthSessionService();
        var documents = CreateDocuments(new OfflineCacheService(), sessions);
        try
        {
            await sessions.SaveAsync(Session());
            Assert.True(documents.CanAttach);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                documents.DownloadAsync("/documents/included.pdf", "Included"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Picker_cancel_and_invalid_file_do_not_change_documents_or_manual_state()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var documents = CreateDocuments(disk, sessions);
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        try
        {
            await sessions.SaveAsync(Session());
            await organizer.SetManualStateAsync("accommodation", PreparationManualState.NotNeeded);
            var canceled = new TripDocumentAttachmentService(documents, organizer, sessions, new FakePicker(() => Task.FromResult<PickedTripDocument?>(null)));
            Assert.Null(await canceled.PickAndAttachAsync(LocalDocumentCategory.Accommodation, "accommodation", "Add"));

            var invalid = new TripDocumentAttachmentService(documents, organizer, sessions, Picker("hotel.pdf", "<html>invalid"));
            await Assert.ThrowsAsync<InvalidDataException>(() => invalid.PickAndAttachAsync(
                LocalDocumentCategory.Accommodation, "accommodation", "Add"));
            Assert.Empty(await documents.ListAsync());
            Assert.Equal(PreparationManualState.NotNeeded, State(await organizer.GetAsync(), "accommodation"));
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Account_change_while_picker_is_open_discards_the_operation()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var documents = CreateDocuments(disk, sessions);
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        var first = Session();
        var second = Session();
        try
        {
            await sessions.SaveAsync(first);
            var picker = new FakePicker(async () =>
            {
                await sessions.SaveAsync(second);
                return Picked("ticket.pdf", "%PDF-1.7 ticket");
            });
            var attachment = new TripDocumentAttachmentService(documents, organizer, sessions, picker);
            Assert.Null(await attachment.PickAndAttachAsync(LocalDocumentCategory.Transport, "transport", "Add"));
            Assert.Empty(await documents.ListAsync());
            await sessions.SaveAsync(first);
            Assert.Empty(await documents.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Trip_change_while_opening_the_selected_file_discards_the_operation()
    {
        var sessions = new AuthSessionService();
        var disk = new OfflineCacheService();
        var documents = CreateDocuments(disk, sessions);
        var organizer = new TripPreparationOrganizerStore(disk, sessions);
        var first = Session();
        try
        {
            await sessions.SaveAsync(first);
            var selected = new PickedTripDocument("ticket.pdf", async () =>
            {
                await sessions.SaveAsync(first with { TripId = Guid.NewGuid() });
                return (Stream)new MemoryStream("%PDF-1.7 ticket"u8.ToArray());
            });
            var attachment = new TripDocumentAttachmentService(documents, organizer, sessions,
                new FakePicker(() => Task.FromResult<PickedTripDocument?>(selected)));
            Assert.Null(await attachment.PickAndAttachAsync(LocalDocumentCategory.Transport, "transport", "Add"));
            Assert.Empty(await documents.ListAsync());
            await sessions.SaveAsync(first);
            Assert.Empty(await documents.ListAsync());
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public async Task Old_documents_are_other_and_personal_documents_can_move_category()
    {
        const string oldJson = """{"Id":"11111111-1111-1111-1111-111111111111","Title":"Old","Extension":".pdf","Size":10,"SavedAt":"2026-01-01T00:00:00+00:00"}""";
        var old = JsonSerializer.Deserialize<LocalTripDocument>(oldJson)!;
        Assert.Equal(LocalDocumentCategory.Other, old.Category ?? LocalDocumentCategory.Other);
        Assert.Null(TripPreparationCategoryCatalog.PreparationKey(LocalDocumentCategory.Other));

        var sessions = new AuthSessionService();
        var documents = CreateDocuments(new OfflineCacheService(), sessions);
        try
        {
            await sessions.SaveAsync(Session());
            var saved = await documents.AttachAsync(new MemoryStream("%PDF-1.7 hotel"u8.ToArray()), "hotel.pdf",
                LocalDocumentCategory.Accommodation);
            await documents.SetCategoryAsync(saved.Id, LocalDocumentCategory.Reservations);
            Assert.Equal(LocalDocumentCategory.Reservations, Assert.Single(await documents.ListAsync()).Category);
        }
        finally { sessions.Clear(); }
    }

    [Fact]
    public void Category_counts_include_files_or_manual_declarations_without_counting_curated_files()
    {
        var personal = new LocalTripDocument(Guid.NewGuid(), "Ticket", ".pdf", 10, DateTimeOffset.UtcNow,
            Category: LocalDocumentCategory.Transport);
        var old = new LocalTripDocument(Guid.NewGuid(), "Old", ".pdf", 10, DateTimeOffset.UtcNow);
        var curated = new LocalTripDocument(Guid.NewGuid(), "Included", ".pdf", 10, DateTimeOffset.UtcNow,
            "/documents/included", LocalDocumentCategory.Transport);

        Assert.Equal(1, TripPreparationOrganizationPolicy.DocumentCount(LocalDocumentCategory.Transport, [personal, old, curated]));
        Assert.Equal(1, TripPreparationOrganizationPolicy.DocumentCount(LocalDocumentCategory.Other, [personal, old, curated]));
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.Pending, 1));
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.NotNeeded, 0));
        Assert.False(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.Pending, 0));
    }

    [Theory]
    [InlineData(PreparationManualState.Pending, false)]
    [InlineData(PreparationManualState.OutsideApp, true)]
    [InlineData(PreparationManualState.NotNeeded, true)]
    public void Missing_personal_document_is_pending_only_without_a_manual_declaration(
        PreparationManualState manualState, bool isOrganized)
    {
        var curated = new LocalTripDocument(Guid.NewGuid(), "Included", ".pdf", 10, DateTimeOffset.UtcNow,
            "/documents/included", LocalDocumentCategory.Transport);
        var hotel = new LocalTripDocument(Guid.NewGuid(), "Hotel", ".pdf", 10, DateTimeOffset.UtcNow,
            Category: LocalDocumentCategory.Accommodation);
        var legacy = new LocalTripDocument(Guid.NewGuid(), "Old", ".pdf", 10, DateTimeOffset.UtcNow);
        var transportCount = TripPreparationOrganizationPolicy.DocumentCount(
            LocalDocumentCategory.Transport, [curated, hotel, legacy]);

        Assert.Equal(0, transportCount);
        Assert.Equal(isOrganized, TripPreparationOrganizationPolicy.IsOrganized(manualState, transportCount));
    }

    [Fact]
    public void Moving_the_last_attachment_changes_organization_in_both_categories_without_clearing_manual_choices()
    {
        var hotel = new LocalTripDocument(Guid.NewGuid(), "Hotel", ".pdf", 10, DateTimeOffset.UtcNow,
            Category: LocalDocumentCategory.Accommodation);
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.Pending,
            TripPreparationOrganizationPolicy.DocumentCount(LocalDocumentCategory.Accommodation, [hotel])));

        var moved = hotel with { Category = LocalDocumentCategory.Reservations };
        var accommodationCount = TripPreparationOrganizationPolicy.DocumentCount(LocalDocumentCategory.Accommodation, [moved]);
        var reservationsCount = TripPreparationOrganizationPolicy.DocumentCount(LocalDocumentCategory.Reservations, [moved]);

        Assert.False(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.Pending, accommodationCount));
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.Pending, reservationsCount));
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.OutsideApp, accommodationCount));
        Assert.True(TripPreparationOrganizationPolicy.IsOrganized(PreparationManualState.NotNeeded, accommodationCount));
    }

    private static PreparationManualState State(PreparationOrganizerState state, string key) =>
        state.Categories.Single(item => item.Key == key).ManualState;

    private static FakePicker Picker(string name, string contents) => new(() => Task.FromResult<PickedTripDocument?>(Picked(name, contents)));
    private static PickedTripDocument Picked(string name, string contents) =>
        new(name, () => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents))));
    private static TripDocumentStore CreateDocuments(OfflineCacheService disk, AuthSessionService sessions) =>
        new(disk, sessions, new TravelCompanionApiClient(), new ReservationDocumentLinkStore(disk, sessions));
    private static AuthSessionDto Session() => new(Guid.NewGuid(), "test@example.com", "Test", false, "token", Guid.NewGuid(),
        AccessMode: SessionAccessMode.BuilderReadOnly, ExperienceMode: ExperienceMode.SelfServiceBuilder,
        Capabilities: new(true, false, false, false, false));

    private sealed class FakePicker(Func<Task<PickedTripDocument?>> pick) : ITripDocumentPicker
    {
        public Task<PickedTripDocument?> PickAsync(string title) => pick();
    }
}
