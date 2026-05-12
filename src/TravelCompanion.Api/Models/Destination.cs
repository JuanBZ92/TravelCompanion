namespace TravelCompanion.Api.Models;

public sealed class Destination
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string Country { get; set; }
    public required string HeroImageUrl { get; set; }
    public required string ShortDescription { get; set; }

    public List<TravelPackage> Packages { get; set; } = [];
    public List<Recommendation> Recommendations { get; set; } = [];
}
