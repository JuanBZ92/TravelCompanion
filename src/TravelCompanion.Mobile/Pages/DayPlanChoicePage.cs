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
        SafeAreaEdges = SafeAreaEdges.All;
        Style AppStyle(string key) => (Style)Application.Current!.Resources[key];
        var content = new VerticalStackLayout { Padding = new Thickness(24, 28), Spacing = 22 };
        var heading = new VerticalStackLayout { Spacing = 8 };
        heading.Add(new Label { Text = Text("AssistantEyebrow"), Style = AppStyle("Eyebrow"), CharacterSpacing = 3 });
        heading.Add(new Label { Text = Title, Style = AppStyle("Headline") });
        heading.Add(new Label { Text = Text("AssistantChangeReason"), Style = AppStyle("Body") });
        content.Add(heading);
        var selectedCards = new VerticalStackLayout { Spacing = 12 };
        var selections = new List<(Guid Id, CheckBox Check)>();
        foreach (var card in cards.Where(card => card.ReservationId.HasValue || (!batch && card.RecommendationId.HasValue)))
        {
            var check = new CheckBox { IsChecked = !batch, IsVisible = batch };
            SemanticProperties.SetDescription(check, card.Title);
            selections.Add((card.ReservationId ?? card.RecommendationId!.Value, check));
            var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 8 };
            row.Add(check);
            row.Add(new Label { Text = card.Title, VerticalOptions = LayoutOptions.Center, Style = AppStyle("SectionTitle") }, 1);
            selectedCards.Add(row);
        }
        content.Add(new Border { Style = AppStyle("Card"), Content = selectedCards });
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
        var filters = new VerticalStackLayout { Spacing = 16 };
        foreach (var (key, picker) in new[] { ("AssistantDistanceCategory", distance), ("AssistantBudgetCategory", budget) })
        {
            var field = new VerticalStackLayout { Spacing = 4 };
            field.Add(new Label { Text = Text(key), Style = AppStyle("Eyebrow") });
            SemanticProperties.SetDescription(picker, Text(key));
            field.Add(picker);
            filters.Add(field);
        }
        content.Add(new Border { Style = AppStyle("Card"), Content = filters });
        content.Add(new Label { Text = Text("AssistantAlternativeHelp"), Style = AppStyle("Metadata") });
        var apply = new Button { Text = Text(batch ? "AssistantChangeSelected" : "AssistantApplyChange"), IsVisible = selections.Count > 0 };
        var cancel = new Button { Text = Text("CommonCancel"), Style = AppStyle("GhostButton") };
        async Task CompleteAsync()
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
                distance.SelectedIndex switch { 1 => "closer", 2 => "farther", _ => null },
                budget.SelectedIndex switch { 1 => "cheaper", 2 => "dearer", _ => null });
            await Navigation.PopModalAsync();
            _completion.TrySetResult(_result);
        }
        apply.Clicked += async (_, _) => await CompleteAsync();
        cancel.Clicked += async (_, _) => { await Navigation.PopModalAsync(); _completion.TrySetResult(null); };
        var actions = new VerticalStackLayout { Padding = new Thickness(24, 12), Spacing = 8 };
        actions.Add(apply);
        actions.Add(cancel);
        var layout = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(new ScrollView { Content = content });
        layout.Add(actions, 0, 1);
        Content = layout;
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
