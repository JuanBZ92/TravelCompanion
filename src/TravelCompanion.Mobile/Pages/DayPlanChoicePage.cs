using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public sealed record DayPlanChoice(IReadOnlyList<Guid> ReservationIds, string? Distance, string? Budget);

public sealed class DayPlanChoicePage : ContentPage
{
    private readonly TaskCompletionSource<DayPlanChoice?> _completion = new();
    private DayPlanChoice? _result;
    private bool _closing;
    public Task<DayPlanChoice?> Result => _completion.Task;

    public DayPlanChoicePage(IReadOnlyList<TravelChatCardViewModel> cards, bool batch)
    {
        string Text(string key) => LocalizationResourceManager.Instance[key];
        Title = Text(batch ? "AssistantDayComplete" : "AssistantChangeOptions");
        BackgroundColor = Color.FromArgb("#F8F3ED");
        SafeAreaEdges = SafeAreaEdges.All;
        var content = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        content.Add(new Label { Text = Title, FontSize = 24, TextColor = Color.FromArgb("#1A1714") });
        var selections = new List<(Guid Id, CheckBox Check)>();
        foreach (var card in cards.Where(card => card.ReservationId.HasValue))
        {
            var check = new CheckBox { IsChecked = !batch, IsVisible = batch };
            SemanticProperties.SetDescription(check, card.Title);
            selections.Add((card.ReservationId!.Value, check));
            var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 8 };
            row.Add(check);
            row.Add(new Label { Text = card.Title, VerticalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#1A1714") }, 1);
            content.Add(row);
        }
        var distance = new Picker
        {
            Title = Text("AssistantDistanceCategory"),
            ItemsSource = new[] { Text("AssistantNoAdjustment"), Text("AssistantCloser"), Text("AssistantFarther") },
            SelectedIndex = 0
        };
        var budget = new Picker
        {
            Title = Text("AssistantBudgetCategory"),
            ItemsSource = new[] { Text("AssistantNoAdjustment"), Text("AssistantCheaper"), Text("AssistantDearer") },
            SelectedIndex = 0
        };
        content.Add(new Label { Text = Text("AssistantChangeReason") });
        content.Add(new Label { Text = Text("AssistantDistanceCategory") });
        content.Add(distance);
        content.Add(new Label { Text = Text("AssistantBudgetCategory") });
        content.Add(budget);
        var apply = new Button { Text = Text(batch ? "AssistantChangeSelected" : "AssistantApplyChange"), IsVisible = selections.Count > 0 };
        var random = new Button { Text = Text("AssistantRandomAlternative"), IsVisible = !batch };
        var cancel = new Button { Text = Text("CommonCancel") };
        async Task CompleteAsync(bool randomized)
        {
            if (_closing) return;
            var ids = selections.Where(item => item.Check.IsChecked).Select(item => item.Id).ToList();
            if (ids.Count == 0)
            {
                await DisplayAlertAsync(Title, Text("AssistantSelectEvents"), "OK");
                return;
            }
            _closing = true;
            _result = new DayPlanChoice(ids,
                randomized ? null : distance.SelectedIndex switch { 1 => "closer", 2 => "farther", _ => null },
                randomized ? null : budget.SelectedIndex switch { 1 => "cheaper", 2 => "dearer", _ => null });
            await Navigation.PopModalAsync();
            _completion.TrySetResult(_result);
        }
        apply.Clicked += async (_, _) => await CompleteAsync(false);
        random.Clicked += async (_, _) => await CompleteAsync(true);
        cancel.Clicked += async (_, _) => { await Navigation.PopModalAsync(); _completion.TrySetResult(null); };
        content.Add(apply);
        content.Add(random);
        content.Add(cancel);
        Content = new ScrollView { Content = content };
    }

    protected override bool OnBackButtonPressed()
    {
        _completion.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_closing) _completion.TrySetResult(null);
    }
}
