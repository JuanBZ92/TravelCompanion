using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Dtos;

public sealed record RecommendationDto(
    Guid Id,
    Guid DestinationId,
    string Title,
    string Category,
    string Neighborhood,
    string Description,
    IReadOnlyList<string> Tags,
    string PriceLevel,
    decimal Latitude,
    decimal Longitude,
    int SuggestedDurationMinutes,
    double? Rating,
    string? OpeningHours,
    ContentAccessLevel AccessLevel,
    IReadOnlyList<Guid> PackageIds,
    decimal? DistanceKm)
{
    public string Provider { get; init; } = "YUKU";
    public string? ProviderPlaceId { get; init; }
    public string? Attribution { get; init; }
    public string? ExtraDescription { get; init; }
    public string? RefinedType { get; init; }
    public string? ReservationInstructions { get; init; }
    public string? OriginalPrice { get; init; }
    public bool IsPriceKnown { get; init; } = true;
    public string? RatingSource { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string SelectionKey => Id != Guid.Empty ? Id.ToString("N") : $"{Provider}:{ProviderPlaceId}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayDescription => string.IsNullOrWhiteSpace(ExtraDescription) ? Description : $"{Description}\n\n{ExtraDescription}";
}

public sealed record PlaceSearchRequest(
    string Query,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? City = null);

public enum PlaceAutocompleteMode
{
    Hotel,
    Place
}

public sealed record PlaceAutocompleteRequest(
    string Query,
    string? City,
    string SessionToken,
    string? Locale = null,
    PlaceAutocompleteMode Mode = PlaceAutocompleteMode.Hotel);
public sealed record PlaceSuggestionDto(string PlaceId, string Name, string Address);
public sealed record PlaceDetailsRequest(string PlaceId, string SessionToken, string? Locale = null);

public sealed record RecommendationTagDto(
    string Tag,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    int RecommendationCount,
    bool IsCategory);
