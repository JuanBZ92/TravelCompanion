using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

public sealed class ImproveDayPage : ContentPage, IQueryAttributable
{
    private DateOnly date;
    private string? city;
    private DayPersonalizationOptionsDto? options;
    private bool navigating;
    private readonly Label context = new() { FontSize = 14, TextColor = ExpenseUi.Muted };
    private readonly VerticalStackLayout choices = new() { Spacing = 14 };

    public ImproveDayPage()
    {
        Title = ExpenseUi.T("Mejorar el día", "Improve the day");
        SafeAreaEdges = SafeAreaEdges.All;
        BackgroundColor = Color.FromArgb("#F7F2EB");
        Shell.SetTabBarIsVisible(this, false);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 24, 24, 32), Spacing = 24,
                Children =
                {
                    new Label { Text = Title, FontFamily = "serif", FontSize = 32, TextColor = ExpenseUi.Ink },
                    context,
                    new Label { Text = ExpenseUi.T("Elegí cómo querés preparar tu día.", "Choose how to prepare your day."), FontSize = 15, TextColor = ExpenseUi.Muted },
                    choices
                }
            }
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Date", out var selected) && selected is DateOnly day) date = day;
        city = query.TryGetValue("City", out var selectedCity) ? selectedCity as string : null;
        options = query.TryGetValue("Options", out var selectedOptions) ? selectedOptions as DayPersonalizationOptionsDto : null;
        context.Text = date.ToString("d MMMM") + (string.IsNullOrWhiteSpace(city) ? "" : $" · {city}");
        choices.Clear();
        Add(ExpenseUi.T("Completar el día", "Complete the day"), ExpenseUi.T("Ideas para los espacios libres, sin mover tus planes.", "Ideas for free time, keeping your plans."), async () =>
            await Shell.Current.GoToAsync("//main/assistant", new ShellNavigationQueryParameters { ["ReviewDate"] = date, ["ReviewCity"] = city ?? string.Empty }), true);
        if (options?.Enabled == true)
            Add(ExpenseUi.T("Personalizar mi día", "Personalize my day"), ExpenseUi.T("Elegí tus intereses, presupuesto y ritmo.", "Choose your interests, budget and pace."), async () =>
                await Shell.Current.GoToAsync(nameof(DayPersonalizationPage), new ShellNavigationQueryParameters { ["Date"] = date, ["City"] = city ?? string.Empty, ["Options"] = options }));
        Add(ExpenseUi.T("Revisar mi día", "Review my day"), ExpenseUi.T("Revisá horarios y resolvé los conflictos de esta fecha.", "Review times and resolve conflicts for this date."), async () =>
            await Shell.Current.GoToAsync(nameof(DayConflictPage), new ShellNavigationQueryParameters { ["ReviewDate"] = date }));
        Add(ExpenseUi.T("Revisar mi viaje", "Review my trip"), ExpenseUi.T("Una revisión de todos los días del itinerario.", "Review every day of your itinerary."), async () =>
            await Shell.Current.GoToAsync(nameof(TripReviewPage)));
    }

    private void Add(string title, string description, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = title + "  ›", FontSize = 17, CornerRadius = 18, MinimumHeightRequest = 56,
            BackgroundColor = Color.FromArgb(primary ? "#3D3329" : "#E9DDCF"), TextColor = primary ? Colors.White : ExpenseUi.Ink };
        button.Clicked += async (_, _) =>
        {
            if (navigating) return;
            navigating = true;
            try { await action(); }
            catch (Exception)
            {
                await DisplayAlertAsync(ExpenseUi.T("No pudimos abrir esta opción", "Couldn't open this option"),
                    ExpenseUi.T("Volvé a intentarlo.", "Please try again."), "OK");
            }
            finally { navigating = false; }
        };
        choices.Add(new VerticalStackLayout { Spacing = 7, Children = { button,
            new Label { Text = description, FontSize = 13, TextColor = ExpenseUi.Muted, Margin = new Thickness(4, 0, 4, 8) } } });
    }
}
