using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Pages;

internal static class ExpenseUi
{
    public static readonly Color Ink = Color.FromArgb("#393226"), Muted = Color.FromArgb("#8E867D"), Gold = Color.FromArgb("#B49468"), Paper = Color.FromArgb("#F8F4ED");
    public static bool English => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    public static string T(string es, string en) => English ? en : es;
    public static string Category(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Food => T("Comida", "Food"), ExpenseCategory.Drinks => T("Café y bebidas", "Drinks"),
        ExpenseCategory.Transport => T("Transporte", "Transport"), ExpenseCategory.Accommodation => T("Alojamiento", "Lodging"),
        ExpenseCategory.Flights => T("Vuelos", "Flights"), ExpenseCategory.Activities => T("Actividades", "Activities"),
        ExpenseCategory.Shopping => T("Compras", "Shopping"), _ => T("Otros", "Other")
    };
    public static string Icon(ExpenseCategory category) => $"expense_{category.ToString().ToLowerInvariant()}.svg";
    public static string Money(decimal value, string currency) => $"{value.ToString("N" + ExpensePolicy.Digits(currency), CultureInfo.CurrentCulture)} {currency}";
    public static Label Text(string text, double size = 14, bool title = false) => new()
    { Text = text, FontSize = size, FontFamily = title ? "serif" : null, TextColor = title ? Ink : Muted };
    public static Border Card(View content) => new() { Content = content, BackgroundColor = Color.FromArgb("#FFFDF9"),
        Stroke = Color.FromArgb("#E6DFD6"), StrokeShape = new RoundRectangle { CornerRadius = 18 }, Padding = 16 };
    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = text, BackgroundColor = primary ? Ink : Colors.Transparent,
            TextColor = primary ? Colors.White : Ink, CornerRadius = 14, MinimumHeightRequest = 48, FontSize = 14 };
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Services.ClientDiagnostics.Record("expense_action_failed", exception: error);
                await Shell.Current.DisplayAlertAsync(T("Gastos", "Expenses"), error is ArgumentException or InvalidOperationException
                    ? error.Message : T("No pudimos completar la acción. Tus cambios locales se conservan.", "Could not complete the action. Local changes are retained."), "OK");
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
}
