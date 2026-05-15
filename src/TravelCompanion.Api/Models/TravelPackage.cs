namespace TravelCompanion.Api.Models;

public sealed class TravelPackage
{
    public Guid Id { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public bool IsSubscription { get; set; }
    public List<Recommendation> Recommendations { get; set; } = [];
}
