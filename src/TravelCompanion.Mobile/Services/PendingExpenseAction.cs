namespace TravelCompanion.Mobile.Services;

public sealed class PendingExpenseAction
{
    public Guid UserId { get; set; }
    public Guid TripId { get; set; }
    public string? Action { get; set; }
}
