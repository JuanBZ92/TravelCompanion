using TravelCompanion.Shared.Dtos;
namespace TravelCompanion.Api.Models;

public sealed class TripExpense
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "JPY";
    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; }
    public string Concept { get; set; } = "";
    public Guid? ActivityId { get; set; }
    public string? ActivityTitle { get; set; }
    public decimal? Rate { get; set; }
    public DateOnly? RateDate { get; set; }
    public string? RateSource { get; set; }
    public string BaseCurrency { get; set; } = "EUR";
    public int Revision { get; set; }
    public Guid MutationId { get; set; }
    public bool Deleted { get; set; }
}
public sealed class TripExpenseSettings
{
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal? Budget { get; set; }
    public int Revision { get; set; }
    public Guid MutationId { get; set; }
}
