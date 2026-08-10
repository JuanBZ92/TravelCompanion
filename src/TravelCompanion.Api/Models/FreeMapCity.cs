namespace TravelCompanion.Api.Models;

public sealed class FreeMapCity
{
    public Guid Id { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string CitySlug { get; set; }
    public required string DisplayName { get; set; }
    public decimal CenterLatitude { get; set; }
    public decimal CenterLongitude { get; set; }
    public decimal FreeRadiusKm { get; set; } = 2m;
    public decimal CoverageRadiusKm { get; set; } = 25m;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? ContactUrl { get; set; }
}
