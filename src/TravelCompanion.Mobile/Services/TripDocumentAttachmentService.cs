namespace TravelCompanion.Mobile.Services;

public sealed record PickedTripDocument(string FileName, Func<Task<Stream>> OpenReadAsync);

public interface ITripDocumentPicker
{
    Task<PickedTripDocument?> PickAsync(string title);
}

public sealed class TripDocumentAttachmentService(
    TripDocumentStore documents,
    TripPreparationOrganizerStore organizer,
    AuthSessionService sessions,
    ITripDocumentPicker picker)
{
    public async Task<LocalTripDocument?> PickAndAttachAsync(
        LocalDocumentCategory category,
        string? preparationKey,
        string pickerTitle,
        CancellationToken ct = default)
    {
        if (!documents.CanAttach) throw new UnauthorizedAccessException();
        var user = sessions.CurrentUserId;
        var trip = sessions.CurrentTripId;
        var contextVersion = sessions.ContextVersion;
        bool IsCurrent() => sessions.HasSession && sessions.CurrentUserId == user && sessions.CurrentTripId == trip
            && sessions.ContextVersion == contextVersion;
        var file = await picker.PickAsync(pickerTitle);
        if (file is null) return null;
        if (!IsCurrent()) return null;
        await using var stream = await file.OpenReadAsync();
        if (!IsCurrent()) return null;
        var document = await documents.AttachAsync(stream, file.FileName, category, ct);
        if (preparationKey is not null)
        {
            try { await organizer.SetManualStateAsync(preparationKey, PreparationManualState.Pending, ct); }
            catch
            {
                try { await documents.DeleteAsync(document.Id); } catch { }
                throw;
            }
        }
        return document;
    }
}
