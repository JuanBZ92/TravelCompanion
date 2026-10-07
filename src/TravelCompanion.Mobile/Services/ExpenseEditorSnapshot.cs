using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record ExpenseEditorSnapshot(string Amount, string Currency, DateOnly Date,
    ExpenseCategory Category, string Concept, string ManualRate, Guid? ActivityId)
{
    public static ExpenseEditorSnapshot Create(string? amount, string currency, DateOnly date,
        ExpenseCategory category, string? concept, string? manualRate, Guid? activityId) =>
        new(amount?.Trim() ?? "", currency, date, category, concept?.Trim() ?? "", manualRate?.Trim() ?? "", activityId);
}
