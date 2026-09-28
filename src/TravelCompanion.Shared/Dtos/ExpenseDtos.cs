namespace TravelCompanion.Shared.Dtos;

public enum ExpenseCategory { Other, Food, Drinks, Transport, Accommodation, Flights, Activities, Shopping }
public sealed record ExpenseDto(Guid Id, Guid TripId, decimal Amount, string Currency, DateOnly Date,
    ExpenseCategory Category, string Concept, Guid? ActivityId, string? ActivityTitle,
    decimal? Rate, DateOnly? RateDate, string? RateSource, string BaseCurrency,
    int Revision, bool Deleted, Guid MutationId);
public sealed record ExpenseSettingsDto(string Currency, decimal? Budget, int Revision, Guid MutationId);
public sealed record ExpensesDto(ExpenseSettingsDto Settings, IReadOnlyList<ExpenseDto> Items,
    bool HasPremium, string TimeZoneId, IReadOnlyList<ExpenseActivityDto> Activities);
public sealed record ExpenseActivityDto(Guid Id, string Title, DateOnly Date, ExpenseCategory Category);
public sealed record SaveExpenseRequest(decimal Amount, string Currency, DateOnly Date, ExpenseCategory Category,
    string Concept, Guid? ActivityId, decimal? ManualRate, int ExpectedRevision, int SettingsRevision,
    Guid MutationId, bool Deleted = false, ExpenseRateDto? CachedRate = null);
public sealed record SaveExpenseResult(bool Saved, ExpenseDto? Entry, bool SettingsChanged = false);
public sealed record SaveExpenseSettingsRequest(string Currency, decimal? Budget, int ExpectedRevision, Guid MutationId);
public sealed record ExpenseRateDto(string Currency, string BaseCurrency, decimal Rate, DateOnly Date, string Source);
public sealed record ExpenseBreakdownDto(IReadOnlyList<ExpenseCategoryTotal> Categories, IReadOnlyList<ExpenseDayTotal> Days, string Currency = "EUR");
public sealed record ExpenseCategoryTotal(ExpenseCategory Category, decimal Total, int Pending);
public sealed record ExpenseDayTotal(DateOnly Date, decimal Total, int Pending);
