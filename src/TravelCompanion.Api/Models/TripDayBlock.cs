namespace TravelCompanion.Api.Models;

public sealed class TripDayBlock
{
    public Guid Id { get; set; }
    public Guid TripDayPlanId { get; set; }
    public TripDayPlan? TripDayPlan { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string CuratedDescription { get; set; } = string.Empty;
    public bool AutofillEnabled { get; set; } = true;
    public List<Reservation> Reservations { get; set; } = [];
}
