namespace TravelCompanion.Api.Models;

public sealed class TripDayPlan
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public DateOnly Date { get; set; }
    public int DayNumber { get; set; }
    public string City { get; set; } = string.Empty;
    public string HotelBase { get; set; } = string.Empty;
    public decimal? BaseLatitude { get; set; }
    public decimal? BaseLongitude { get; set; }
    public string Introduction { get; set; } = string.Empty;
    public List<TripDayBlock> Blocks { get; set; } = [];
}
