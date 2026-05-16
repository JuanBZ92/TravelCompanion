using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class Recommendation
{
    public Guid Id { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string Title { get; set; }
    public required string Category { get; set; }
    public required string Neighborhood { get; set; }
    public required string Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public string PriceLevel { get; set; } = "medium";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int SuggestedDurationMinutes { get; set; }
    public double? Rating { get; set; }
    public string? OpeningHours { get; set; }
    public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Free;
    public List<TravelPackage> Packages { get; set; } = [];
}
