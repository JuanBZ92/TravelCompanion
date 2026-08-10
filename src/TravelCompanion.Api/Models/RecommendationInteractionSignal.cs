using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Models;

public sealed class RecommendationInteractionSignal
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid RecommendationId { get; set; }
    public Recommendation? Recommendation { get; set; }
    public RecommendationSignal Signal { get; set; }
    public string Source { get; set; } = "mobile";
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? DistanceMeters { get; set; }
    public decimal? Confidence { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
