using TravelCompanion.Shared.Dtos;
using static TravelCompanion.Mobile.Pages.ExpenseUi;

namespace TravelCompanion.Mobile.Pages;

public sealed class ExpenseBreakdownPage : TripScopedPage
{
    public ExpenseBreakdownPage(ExpenseBreakdownDto data, bool pendingSync)
    {
        BackgroundColor = Paper;
        SafeAreaEdges = SafeAreaEdges.All;
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        heading.Add(EditorialUi.Heading(T("En qué gastaste", "Spending breakdown")));
        heading.Add(EditorialUi.Icon("action_close.svg", T("Cerrar", "Close"), () => Navigation.PopModalAsync()), 1);
        var header = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 8, 0, 20) };
        header.Add(Text(T("Resumen de los gastos guardados en este dispositivo.", "Summary of expenses saved on this device.")));
        if (pendingSync) header.Add(Text(T("Incluye gastos pendientes de sincronizar.", "Includes expenses pending sync."), 13));
        var missing = data.Categories.Sum(x => x.Pending);
        if (missing > 0) header.Add(Text(T($"Total parcial · {missing} sin conversión", $"Partial total · {missing} awaiting conversion"), 13));
        var total = data.Categories.Sum(x => x.Total);
        foreach (var group in data.Categories.OrderByDescending(x => x.Total))
        {
            var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
            row.Add(Text(Category(group.Category), 16));
            row.Add(Text(Money(group.Total, data.Currency) + (group.Pending > 0 ? " ≈" : ""), 16), 1);
            header.Add(row);
            header.Add(new ProgressBar { Progress = total > 0 ? (double)(group.Total / total) : 0, ProgressColor = Gold });
        }
        header.Add(EditorialUi.Heading(T("Por día", "By day"), 24, SemanticHeadingLevel.Level2));
        var list = new CollectionView
        {
            Header = header, ItemsSource = data.Days, SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var day = Text("", 16);
                var amount = Text("", 16);
                var row = new Grid { Padding = new Thickness(0, 10), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
                row.Add(day); row.Add(amount, 1);
                row.BindingContextChanged += (_, _) =>
                {
                    if (row.BindingContext is not ExpenseDayTotal item) return;
                    day.Text = item.Date.ToString("d MMMM");
                    amount.Text = Money(item.Total, data.Currency) + (item.Pending > 0 ? " ≈" : "");
                };
                return row;
            })
        };
        var layout = new Grid { Padding = new Thickness(24, 16), RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], RowSpacing = 12 };
        layout.Add(heading); layout.Add(list, 0, 1); Content = layout;
    }
}
