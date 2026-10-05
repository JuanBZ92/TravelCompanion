using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class DocsViewModel
{
    public LocalDocumentCategory? SelectedCategory { get; private set; }
    public void SetCategory(LocalDocumentCategory? category)
    {
        SelectedCategory = category;
        LocalDocumentGroups.Clear();
        NotifySectionsChanged();
    }
    public ObservableCollection<LocalDocumentGroupViewModel> LocalDocumentGroups { get; } = [];
    public string CopyCodeText => Text("CopyConfirmationCode");
    public bool ShowLocalNotice => sessionService.CurrentTripId.HasValue;
    public bool CanAttachDocument => documentStore.CanAttach;
    public string LocalDocumentsNotice => Text("LocalDocumentsNotice");
    public string AddDocumentText => Text("AddDocument");
    public string DocumentLimitText => Text("DocumentLimit");

    private static string Text(string key) => LocalizationResourceManager.Instance[key];
    private static string CategoryName(LocalDocumentCategory category) => Text("DocumentCategory_" + category);

    private async Task RefreshLocalDocumentsAsync(CancellationToken ct)
    {
        var user = sessionService.CurrentUserId;
        var trip = sessionService.CurrentTripId;
        var contextVersion = sessionService.ContextVersion;
        var selectedCategory = SelectedCategory;
        if (!sessionService.HasSession || !user.HasValue || !trip.HasValue)
        {
            LocalDocumentGroups.Clear();
            RebuildDocumentGroups();
            return;
        }
        var documents = await documentStore.ListAsync(ct);
        if (!IsCurrentDocumentContext(contextVersion, user, trip) || selectedCategory != SelectedCategory) return;
        LocalDocumentGroups.Clear();
        var personal = LocalDocumentPolicy.PersonalDocuments(documents, SelectedCategory).ToList();
        StatusMessage = SelectedCategory.HasValue && personal.Count == 0 ? Text("NoDocuments") : null;
        foreach (var category in Enum.GetValues<LocalDocumentCategory>())
        {
            if (SelectedCategory.HasValue && SelectedCategory.Value != category) continue;
            var rows = personal.Where(item => (item.Category ?? LocalDocumentCategory.Other) == category)
                .Select(item => new LocalDocumentItemViewModel(item, documentStore,
                    () => RefreshLocalDocumentsAsync(default), SelectCategoryAsync)).ToList();
            if (rows.Count > 0) LocalDocumentGroups.Add(new(CategoryName(category), rows));
        }
        RebuildDocumentGroups();
    }

    private async Task<LocalDocumentCategory?> SelectCategoryAsync(LocalDocumentCategory? current = null)
    {
        var categories = Enum.GetValues<LocalDocumentCategory>();
        var labels = categories.Select(CategoryName).ToArray();
        var selected = await Shell.Current.DisplayActionSheetAsync(
            Text("ChooseDocumentCategory"), Text("CommonCancel"), null, labels);
        var index = Array.IndexOf(labels, selected);
        return index < 0 ? null : categories[index];
    }

    private async Task AttachToCategoryAsync(LocalDocumentCategory category, CancellationToken ct)
    {
        var key = TripPreparationCategoryCatalog.PreparationKey(category);
        var saved = await attachmentService.PickAndAttachAsync(category, key,
            string.Format(Text("AttachDocumentToCategory"), CategoryName(category)), ct);
        if (saved is null) return;
        await RefreshLocalDocumentsAsync(ct);
        StatusMessage = string.Format(Text("DocumentSavedInCategory"), CategoryName(category));
        SemanticScreenReader.Default.Announce(StatusMessage);
    }

    private async Task RefreshDocumentAvailabilityAsync(CancellationToken ct)
    {
        foreach (var document in HotelDocuments.Concat(OtherDocuments).ToList()) await document.RefreshAsync(ct);
    }

    [RelayCommand]
    private Task AttachDocumentAsync() => base.LoadAsync(async ct =>
    {
        if (!CanAttachDocument) return;
        var category = SelectedCategory ?? await SelectCategoryAsync();
        if (!category.HasValue) return;
        try { await AttachToCategoryAsync(category.Value, ct); }
        catch (IOException) { ErrorMessage = Text("DocumentError") + " " + Text("DocumentLimit"); }
    });
}

public sealed record LocalDocumentGroupViewModel(string Name, IReadOnlyList<LocalDocumentItemViewModel> Documents);

public sealed class LocalDocumentItemViewModel
{
    public LocalDocumentItemViewModel(LocalTripDocument document, TripDocumentStore store, Func<Task> refresh,
        Func<LocalDocumentCategory?, Task<LocalDocumentCategory?>> chooseCategory)
    {
        Title = document.Title;
        Details = $"{CategoryName(document.Category ?? LocalDocumentCategory.Other)} · {document.Extension.TrimStart('.').ToUpperInvariant()} · {document.Size / 1024d:N0} KB";
        OpenCommand = new AsyncRelayCommand(() => ExecuteAsync(() => store.OpenAsync(document.Id)));
        RenameCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            var name = await Shell.Current.DisplayPromptAsync(Text("RenameDocument"), Text("DocumentRenamePrompt"),
                "OK", Text("CommonCancel"), initialValue: document.Title, maxLength: 120);
            if (string.IsNullOrWhiteSpace(name)) return;
            await store.RenameAsync(document.Id, name); await refresh();
        }));
        MoveCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            var category = await chooseCategory(document.Category ?? LocalDocumentCategory.Other);
            if (!category.HasValue || category == document.Category) return;
            await store.SetCategoryAsync(document.Id, category.Value); await refresh();
        }));
        DeleteCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            if (!await Shell.Current.DisplayAlertAsync(Text("DeleteLocalCopy"), document.Title,
                Text("DeleteLocalCopy"), Text("CommonCancel"))) return;
            await store.DeleteAsync(document.Id); await refresh();
        }));
        MoreCommand = new AsyncRelayCommand(() => ExecuteAsync(async () =>
        {
            var choice = await Shell.Current.DisplayActionSheetAsync(document.Title, Text("CommonCancel"), null,
                RenameText, MoveText, DeleteText);
            if (choice == RenameText) await RenameCommand.ExecuteAsync(null);
            else if (choice == MoveText) await MoveCommand.ExecuteAsync(null);
            else if (choice == DeleteText) await DeleteCommand.ExecuteAsync(null);
        }));
    }
    public string Title { get; }
    public string Details { get; }
    public string OpenDescription => string.Format(Text("OpenDocumentNamed"), Title);
    public string MoreDescription => string.Format(Text("DocumentActionsNamed"), Title);
    public IAsyncRelayCommand MoreCommand { get; }
    public string RenameText => Text("RenameDocument");
    public string MoveText => Text("MoveDocumentCategory");
    public string DeleteText => Text("DeleteLocalCopy");
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand MoveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    private static string Text(string key) => LocalizationResourceManager.Instance[key];
    private static string CategoryName(LocalDocumentCategory category) => Text("DocumentCategory_" + category);
    private static async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); }
        catch { await Shell.Current.DisplayAlertAsync(Text("LocalDocuments"), Text("DocumentUnavailable"), "OK"); }
    }
}
