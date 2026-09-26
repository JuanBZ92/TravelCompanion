using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DocsViewModel
{
    public ObservableCollection<LocalDocumentItemViewModel> LocalDocuments { get; } = [];
    public bool CanAttachDocument => documentStore.CanAttach;
    public string LocalDocumentsTitle => Text("LocalDocuments");
    public string LocalDocumentsNotice => Text("LocalDocumentsNotice");
    public string AttachDocumentText => Text("AttachDocument");
    public string DocumentLimitText => Text("DocumentLimit");

    private static string Text(string key) => LocalizationResourceManager.Instance[key];

    private async Task RefreshLocalDocumentsAsync(CancellationToken ct)
    {
        var user = sessionService.CurrentUserId;
        var trip = sessionService.CurrentTripId;
        if (!user.HasValue || !trip.HasValue) return;
        var documents = await documentStore.ListAsync(ct);
        if (user != sessionService.CurrentUserId || trip != sessionService.CurrentTripId) return;
        LocalDocuments.Clear();
        foreach (var document in documents.Where(item => item.SourceUrl is null))
            LocalDocuments.Add(new LocalDocumentItemViewModel(document, documentStore, () => RefreshLocalDocumentsAsync(ct)));
    }

    private async Task RefreshDocumentAvailabilityAsync(CancellationToken ct)
    {
        foreach (var document in HotelDocuments.Concat(OtherDocuments).ToList()) await document.RefreshAsync(ct);
    }

    [RelayCommand]
    private Task AttachDocumentAsync() => base.LoadAsync(async ct =>
    {
        if (!CanAttachDocument) return;
        var scope = (sessionService.CurrentUserId, sessionService.CurrentTripId);
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = Text("AttachDocument"),
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/pdf", "image/jpeg", "image/png"],
                [DevicePlatform.iOS] = ["com.adobe.pdf", "public.jpeg", "public.png"],
                [DevicePlatform.MacCatalyst] = ["com.adobe.pdf", "public.jpeg", "public.png"],
                [DevicePlatform.WinUI] = [".pdf", ".jpg", ".jpeg", ".png"]
            })
        });
        if (file is null || scope != (sessionService.CurrentUserId, sessionService.CurrentTripId)) return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            await documentStore.AttachAsync(stream, file.FileName, ct);
            await RefreshLocalDocumentsAsync(ct);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        { ErrorMessage = Text("DocumentError") + " " + Text("DocumentLimit"); }
    });
}

public sealed class LocalDocumentItemViewModel
{
    public LocalDocumentItemViewModel(LocalTripDocument document, TripDocumentStore store, Func<Task> refresh)
    {
        Title = document.Title;
        Details = $"{document.Extension.TrimStart('.').ToUpperInvariant()} · {document.Size / 1024d:N0} KB";
        OpenCommand = new AsyncRelayCommand(() => ExecuteAsync(() => store.OpenAsync(document.Id)));
        RenameCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            var name = await Shell.Current.DisplayPromptAsync(Text("RenameDocument"), Text("DocumentRenamePrompt"),
                "OK", Text("CommonCancel"), initialValue: document.Title, maxLength: 120);
            if (string.IsNullOrWhiteSpace(name)) return;
            await store.RenameAsync(document.Id, name); await refresh();
        }));
        DeleteCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            if (!await Shell.Current.DisplayAlertAsync(Text("DeleteLocalCopy"), document.Title, Text("DeleteLocalCopy"), Text("CommonCancel"))) return;
            await store.DeleteAsync(document.Id); await refresh();
        }));
    }
    public string Title { get; }
    public string Details { get; }
    public string RenameText => Text("RenameDocument");
    public string DeleteText => Text("DeleteLocalCopy");
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    private static string Text(string key) => LocalizationResourceManager.Instance[key];
    private static async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); }
        catch { await Shell.Current.DisplayAlertAsync(Text("LocalDocuments"), Text("DocumentUnavailable"), "OK"); }
    }
}
