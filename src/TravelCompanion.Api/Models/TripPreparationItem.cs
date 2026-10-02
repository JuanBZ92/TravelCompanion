namespace TravelCompanion.Api.Models;

public sealed class TripPreparationItem
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public string Key { get; set; } = "";
    public bool Completed { get; set; }
    public int Revision { get; set; }
}
