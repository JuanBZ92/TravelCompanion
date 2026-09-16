using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class Recommendation
{
    public Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string? ProviderPlaceId { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ExtraDescription { get; set; }
    public string? ExtraDescriptionEn { get; set; }
    public string? RefinedType { get; set; }
    public string? RefinedTypeEn { get; set; }
    public string? ReservationInstructions { get; set; }
    public string? ReservationInstructionsEn { get; set; }
    public string? OriginalPrice { get; set; }
    public bool IsPriceKnown { get; set; } = true;
    public int? VerificationConfidence { get; set; }
    public string? ReservationSource { get; set; }
    public string? VerificationNotes { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public required string Title { get; set; }
    public required string Category { get; set; }
    public required string Neighborhood { get; set; }
    public string? CitySlug { get; set; }
    public required string Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public string PriceLevel { get; set; } = "medium";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int SuggestedDurationMinutes { get; set; }
    public double? Rating { get; set; }
    public string? OpeningHours { get; set; }
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }
    public string? CurationNotes { get; set; }
    public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Free;
    public List<TravelPackage> Packages { get; set; } = [];
}
