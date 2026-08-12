using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record BuilderTripSetupSegmentDto(
    [property: Required, MaxLength(120)] string City,
    DateOnly StartsOn,
    DateOnly EndsOn,
    [property: MaxLength(180)] string? HotelName = null,
    [property: MaxLength(300)] string? HotelAddress = null,
    decimal? HotelLatitude = null,
    decimal? HotelLongitude = null,
    [property: MaxLength(160)] string? HotelPlaceId = null);

public sealed record BuilderTripSetupDto(
    bool IsConfigured,
    Guid? TripId,
    int Revision,
    DateOnly? ArrivalDate,
    DateOnly? DepartureDate,
    string Destination,
    string TimeZoneId,
    IReadOnlyList<BuilderTripSetupSegmentDto> Segments);

public sealed record SaveBuilderTripSetupRequest(
    DateOnly ArrivalDate,
    DateOnly DepartureDate,
    [property: MaxLength(120)] string TimeZoneId,
    int ExpectedRevision,
    IReadOnlyList<BuilderTripSetupSegmentDto> Segments);

public sealed record ItineraryItemMutationRequest(
    Guid? RecommendationId,
    [property: MaxLength(160)] string? GooglePlaceId,
    [property: Required, MaxLength(160)] string Title,
    DateOnly Date,
    [property: Required, MaxLength(32)] string PeriodKey,
    bool UseExactTime,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    [property: MaxLength(120)] string? City,
    [property: MaxLength(160)] string? LocationName,
    [property: MaxLength(300)] string? Address,
    [property: MaxLength(2000)] string? Notes,
    decimal? Latitude,
    decimal? Longitude,
    int ExpectedRevision,
    [property: Required, MaxLength(80)] string IdempotencyKey,
    bool ConfirmOverlap = false);

public sealed record ItineraryItemMutationResponse(
    bool Success,
    string Message,
    int Revision,
    ScheduleItemDto? Item = null,
    bool HasOverlap = false);
