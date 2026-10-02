using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class JournalReadingPage(JournalScope scope, JournalMemory original, ScheduleItemDto? item) : JournalScopedPage
{
    private readonly JournalStore store = MauiProgram.Services.GetRequiredService<JournalStore>();
    private JournalMemory memory = original;
    protected override async void OnAppearing()
    {
        base.OnAppearing(); BackgroundColor = JournalUi.Paper; SafeAreaEdges = SafeAreaEdges.All;
        try {
            var memories = await store.LoadAsync(scope, [], false, default);
            var current = memories.FirstOrDefault(x => x.Key == memory.Key);
            if (current is null && memory.IsFree && !memory.IsDraft) { await Navigation.PopModalAsync(); return; }
            memory = current ?? memory;
            var body = new VerticalStackLayout { Padding = 24, Spacing = 20 };
            var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
            header.Add(JournalUi.Text(memory.Date.ToString("d MMMM yyyy"), 13));
            header.Add(JournalUi.Icon("action_close.svg", JournalText.Get("JournalClose"), () => Navigation.PopModalAsync()), 1);
            body.Add(header); body.Add(JournalUi.Text(JournalText.Title(memory), 32, true));
            if (!string.IsNullOrWhiteSpace(memory.City)) body.Add(JournalUi.Text(memory.City, 15));
            if (memory.Images.Length > 0) {
                var index = Math.Max(0, Array.FindIndex(memory.Images, x => x.Id == memory.CoverId));
                var bytes = await store.PhotoAsync(scope, memory.Images[index].Id, true);
                var cover = JournalUi.Icon("journal_photo.svg", JournalText.Get("JournalOpenPhotos"),
                    () => Navigation.PushModalAsync(new JournalPhotoPage(scope, memory, index)));
                cover.WidthRequest = -1; cover.HeightRequest = 280; cover.Padding = 0; cover.Aspect = Aspect.AspectFill;
                if (bytes is not null) cover.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                body.Add(cover); body.Add(JournalUi.Text(JournalText.Format("JournalPhotoCount", memory.Images.Length), 12));
            }
            if (!string.IsNullOrWhiteSpace(memory.Text)) body.Add(JournalUi.Text(memory.Text, 18));
            if (memory.Status.Length > 0) body.Add(JournalUi.Text(memory.Status, 13));
            body.Add(JournalUi.Action("JournalEdit", () => Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item))));
            body.Add(JournalUi.Action("JournalAddPhotos", () => Navigation.PushModalAsync(new JournalMemoryPage(scope, memory, item, true))));
            if (item is not null) body.Add(JournalUi.Action("JournalActivity", async () => {
                if (!store.IsCurrent(scope)) return;
                await Navigation.PopModalAsync();
                await Shell.Current.GoToAsync(nameof(ScheduleItemDetailPage), new Dictionary<string, object> { ["ScheduleItem"] = item });
            }));
            if (memory.IsFree) body.Add(JournalUi.Action("JournalDelete", async () => {
                if (!await DisplayAlertAsync(JournalText.Get("JournalDelete"), JournalText.Get("JournalDeleteQuestion"), JournalText.Get("JournalRemove"), JournalText.Get("JournalCancel"))) return;
                await store.DeleteFreeLocalAsync(scope, memory);
                await store.DiscardDraftAsync(scope, memory);
                await store.LoadAsync(scope, [], Connectivity.Current.NetworkAccess == NetworkAccess.Internet, default);
                await Navigation.PopModalAsync();
            }));
            body.Add(JournalUi.Text(JournalText.Get("JournalStorageNotice"), 12));
            if (store.IsCurrent(scope)) Content = new ScrollView { Content = body };
        } catch (OperationCanceledException) { }
        catch (Exception) { Content = JournalUi.Text(JournalText.Get("JournalFailure")); }
    }
}
