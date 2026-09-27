using System.Globalization;
using Microsoft.Maui.Layouts;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class DayPersonalizationPage : ContentPage, IQueryAttributable
{
    private static readonly (string Key, string Es, string En)[] KnownInterests =
    [
        ("food", "Gastronomía", "Food"), ("culture", "Cultura", "Culture"),
        ("nature", "Naturaleza", "Nature"), ("history", "Historia", "History"),
        ("art", "Arte", "Art"), ("shopping", "Compras", "Shopping"),
        ("gardens", "Jardines", "Gardens"), ("nightlife", "Vida nocturna", "Nightlife")
    ];

    private readonly Picker _pace = new() { Title = "Ritmo" };
    private readonly Picker _budget = new() { Title = "Presupuesto" };
    private readonly FlexLayout _interestChoices = new() { Wrap = FlexWrap.Wrap, Direction = FlexDirection.Row };
    private readonly HashSet<string> _selectedInterests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _interestButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _savePreferences = new();
    private readonly Label _error = new() { TextColor = Color.FromArgb("#9B443E"), FontSize = 12, IsVisible = false };
    private readonly Button _continue = new()
    {
        Text = "Preparar propuesta", BackgroundColor = Color.FromArgb("#A67B43"),
        TextColor = Colors.White, CornerRadius = 18, MinimumHeightRequest = 54
    };
    private DayPersonalizationOptionsDto? _options;
    private DateOnly _date;
    private string? _city;
    private bool _busy;
    private bool English => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public DayPersonalizationPage()
    {
        Title = "Personalizar mi día";
        BackgroundColor = Color.FromArgb("#F7F2EB");
        _pace.ItemsSource = English ? new[] { "Relaxed", "Balanced", "Active" } : ["Tranquilo", "Equilibrado", "Intenso"];
        _budget.ItemsSource = English ? new[] { "Low", "Medium", "High" } : ["Bajo", "Medio", "Alto"];
        _continue.Clicked += OnContinueClicked;
        var content = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(24, 28, 24, 32) };
        content.Add(new Label { Text = English ? "A day that fits you" : "Un día a tu medida",
            FontFamily = "serif", FontSize = 30, TextColor = Color.FromArgb("#1A1714") });
        content.Add(new Label { Text = English
            ? "Choose a pace and the places you enjoy. Your saved plans stay in place."
            : "Elegí tu ritmo y lo que te interesa. Tus planes guardados quedan en su lugar.",
            FontSize = 14, TextColor = Color.FromArgb("#6E675F") });
        content.Add(Field(English ? "PACE" : "RITMO", _pace));
        content.Add(Field(English ? "BUDGET" : "PRESUPUESTO", _budget));
        content.Add(Field(English ? "INTERESTS · UP TO 3" : "INTERESES · HASTA 3", _interestChoices));
        var saveRow = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(_savePreferences, English
            ? "Save these as my general preferences" : "Guardar también como preferencias generales");
        saveRow.Add(_savePreferences);
        saveRow.Add(new Label { Text = English ? "Save these as my general preferences" : "Guardar también como preferencias generales",
            VerticalOptions = LayoutOptions.Center, FontSize = 12, TextColor = Color.FromArgb("#50473E") });
        content.Add(saveRow);
        content.Add(_error);
        content.Add(_continue);
        Content = new ScrollView { Content = content };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Date", out var date) && date is DateOnly selectedDate) _date = selectedDate;
        _city = query.TryGetValue("City", out var city) ? city as string : null;
        _options = query.TryGetValue("Options", out var options) ? options as DayPersonalizationOptionsDto : null;
        var profile = _options?.Profile;
        _pace.SelectedIndex = profile?.TravelPace switch { "relaxed" => 0, "efficient" => 2, _ => 1 };
        _budget.SelectedIndex = profile?.BudgetLevel switch { "low" => 0, "high" => 2, _ => 1 };
        _selectedInterests.Clear();
        foreach (var interest in profile?.Interests.Take(3) ?? [])
            if (!string.IsNullOrWhiteSpace(interest)) _selectedInterests.Add(interest.Trim());
        BuildInterests();
    }

    private static VerticalStackLayout Field(string label, View control)
    {
        var field = new VerticalStackLayout { Spacing = 8 };
        field.Add(new Label { Text = label, CharacterSpacing = 2, FontSize = 10,
            TextColor = Color.FromArgb("#9D794F") });
        field.Add(control);
        return field;
    }

    private void BuildInterests()
    {
        _interestChoices.Children.Clear();
        _interestButtons.Clear();
        var choices = KnownInterests.Select(value => (value.Key, Label: English ? value.En : value.Es))
            .Concat(_selectedInterests.Where(value => KnownInterests.All(known =>
                !known.Key.Equals(value, StringComparison.OrdinalIgnoreCase))).Select(value => (Key: value, Label: value)));
        foreach (var (key, label) in choices)
        {
            var chip = new Button { Text = label, FontSize = 12, CornerRadius = 16,
                MinimumHeightRequest = 38, WidthRequest = Math.Max(78, label.Length * 8.5 + 34),
                Padding = new Thickness(13, 6), Margin = new Thickness(0, 0, 8, 8) };
            chip.Clicked += (_, _) =>
            {
                if (!_selectedInterests.Remove(key))
                {
                    if (_selectedInterests.Count == 3)
                    {
                        ShowError(English ? "Choose up to three interests." : "Elegí hasta tres intereses.");
                        return;
                    }
                    _selectedInterests.Add(key);
                }
                _error.IsVisible = false;
                UpdateChip(key);
            };
            _interestButtons[key] = chip;
            UpdateChip(key);
            _interestChoices.Children.Add(chip);
        }
    }

    private void UpdateChip(string key)
    {
        var chip = _interestButtons[key];
        var selected = _selectedInterests.Contains(key);
        chip.BackgroundColor = Color.FromArgb(selected ? "#3D3329" : "#F8F4EE");
        chip.TextColor = selected ? Colors.White : Color.FromArgb("#3D3329");
        chip.BorderColor = Color.FromArgb("#D8C7B2");
        chip.BorderWidth = selected ? 0 : 1;
    }

    private void ShowError(string message) { _error.Text = message; _error.IsVisible = true; }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (_busy || _options?.Enabled != true || _date == default) return;
        _busy = true;
        _continue.IsEnabled = false;
        try
        {
            var token = await MauiProgram.Services.GetRequiredService<AuthSessionService>().GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) { ShowError("Iniciá sesión nuevamente."); return; }
            var criteria = new GuidedPlanCriteriaDto(Budget: new[] { "low", "medium", "high" }[_budget.SelectedIndex])
            {
                TravelPace = new[] { "relaxed", "balanced", "efficient" }[_pace.SelectedIndex],
                Interests = _selectedInterests.Take(3).ToArray()
            };
            if (_savePreferences.IsChecked)
            {
                var updated = await MauiProgram.Services.GetRequiredService<TravelCompanionApiClient>()
                    .PatchTravelPreferenceProfileAsync(token,
                        new TravelPreferenceProfilePatchDto(null, null, criteria.Budget, criteria.TravelPace,
                            criteria.Interests, null, null, null));
                if (updated is null) { ShowError("No pudimos guardar tus preferencias. Intentá nuevamente."); return; }
            }
            var sessions = MauiProgram.Services.GetRequiredService<AuthSessionService>();
            await MauiProgram.Services.GetRequiredService<ProductAnalyticsTracker>()
                .TrackAsync("personalization_requested", "schedule", tripId: sessions.CurrentTripId);
            if (sessions.IsFreeMapPreview && !_options.FreeTrialAvailable)
            {
                MauiProgram.Services.GetRequiredService<PendingItineraryActionStore>()
                    .SetPersonalization(_date, _city, criteria);
                await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
                return;
            }
            await Shell.Current.GoToAsync("//main/assistant", new ShellNavigationQueryParameters
            {
                ["ReviewDate"] = _date, ["ReviewCity"] = _city ?? string.Empty,
                ["PersonalizedCriteria"] = criteria
            });
        }
        catch (Exception) { ShowError(English ? "Could not prepare the day. Try again." : "No pudimos preparar el día. Intentá nuevamente."); }
        finally { _busy = false; _continue.IsEnabled = true; }
    }
}
