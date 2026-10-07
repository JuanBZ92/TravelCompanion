using System.Globalization;
using System.Text;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared;

public static class ExpensePolicy
{
    public static readonly string[] Currencies = ["JPY", "EUR", "USD", "GBP", "ARS", "AUD", "CAD", "CHF", "CLP", "CNY", "COP", "KRW", "MXN", "BRL", "NZD", "SGD", "TWD", "HKD", "INR"];
    public static int Digits(string currency) => currency is "JPY" or "KRW" or "CLP" ? 0 : 2;
    public static decimal Round(decimal amount, string currency) => Math.Round(amount, Digits(currency), MidpointRounding.AwayFromZero);
    public static decimal? Converted(ExpenseDto item) => item.Deleted ? 0m : item.Rate is { } rate ? Round(item.Amount * rate, item.BaseCurrency) : null;
    public static decimal Total(IEnumerable<ExpenseDto> items) => items.Where(x => !x.Deleted).Sum(x => Converted(x) ?? 0m);
    public static ExpenseBreakdownDto Breakdown(IEnumerable<ExpenseDto> items, string currency)
    {
        var current = items.Where(x => !x.Deleted).ToArray();
        decimal Sum(IEnumerable<ExpenseDto> group) => group.Where(x => x.BaseCurrency == currency).Sum(x => Converted(x) ?? 0m);
        int Pending(IEnumerable<ExpenseDto> group) => group.Count(x => x.Rate is null || x.BaseCurrency != currency);
        return new(current.GroupBy(x => x.Category).Select(g => new ExpenseCategoryTotal(g.Key, Sum(g), Pending(g))).ToArray(),
            current.GroupBy(x => x.Date).Select(g => new ExpenseDayTotal(g.Key, Sum(g), Pending(g))).OrderBy(x => x.Date).ToArray(), currency);
    }
    public static bool TryAmount(string text, out decimal amount) => decimal.TryParse(text.Trim().Replace(',', '.'),
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount > 0 && amount <= 999999999m;
    public static void Validate(SaveExpenseRequest request)
    {
        if (request.MutationId == Guid.Empty || request.ExpectedRevision < 0 || request.SettingsRevision < 0
            || !Currencies.Contains(request.Currency) || request.Amount <= 0 || request.Amount > 999999999m
            || Round(request.Amount, request.Currency) != request.Amount || !Enum.IsDefined(request.Category)
            || request.Concept is null || request.Concept.Length > 160 || request.ManualRate is <= 0 or > 100000000m
            || request.Date.Year < 2000 || request.Date.Year > 2100)
            throw new ArgumentException("El importe, la moneda o la fecha no son válidos.");
        if (request.CachedRate is { } cached && (cached.Currency != request.Currency || !Currencies.Contains(cached.BaseCurrency)
            || cached.Rate <= 0 || cached.Rate > 100000000m || cached.Date > request.Date || cached.Source is not ("Frankfurter" or "cached")))
            throw new ArgumentException("La cotización guardada no es válida.");
    }
    public static string Csv(IEnumerable<ExpenseDto> items)
    {
        static string Cell(string? text)
        {
            text ??= "";
            if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var output = new StringBuilder("Date,Concept,Category,Activity,Amount,Currency,Rate,RateDate,RateSource,Converted,BaseCurrency\r\n");
        foreach (var x in items.Where(x => !x.Deleted).OrderBy(x => x.Date))
            output.AppendLine(string.Join(',', new[] { x.Date.ToString("yyyy-MM-dd"), x.Concept, x.Category.ToString(), x.ActivityTitle,
                x.Amount.ToString(CultureInfo.InvariantCulture), x.Currency, x.Rate?.ToString(CultureInfo.InvariantCulture),
                x.RateDate?.ToString("yyyy-MM-dd"), x.RateSource, Converted(x)?.ToString(CultureInfo.InvariantCulture), x.BaseCurrency }.Select(Cell)));
        return output.ToString();
    }
}
