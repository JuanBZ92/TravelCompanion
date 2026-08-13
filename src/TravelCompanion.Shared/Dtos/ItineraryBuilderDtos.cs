using System.ComponentModel.DataAnnotations;

namespace TravelCompanion.Shared.Dtos;

public sealed record BuilderTripSetupSegmentDto(
    [param: Required, MaxLength(120)] string City,
    DateOnly StartsOn,
    DateOnly EndsOn,
    [param: MaxLength(180)] string? HotelName = null,
    [param: MaxLength(300)] string? HotelAddress = null,
    decimal? HotelLatitude = null,
    decimal? HotelLongitude = null,
    [param: MaxLength(160)] string? HotelPlaceId = null);

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
    [param: MaxLength(120)] string TimeZoneId,
    int ExpectedRevision,
    IReadOnlyList<BuilderTripSetupSegmentDto> Segments);

public sealed record ItineraryItemMutationRequest(
    Guid? RecommendationId,
    [param: MaxLength(160)] string? GooglePlaceId,
    [param: Required, MaxLength(160)] string Title,
    DateOnly Date,
    [param: Required, MaxLength(32)] string PeriodKey,
    bool UseExactTime,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    [param: MaxLength(120)] string? City,
    [param: MaxLength(160)] string? LocationName,
    [param: MaxLength(300)] string? Address,
    [param: MaxLength(2000)] string? Notes,
    decimal? Latitude,
    decimal? Longitude,
    int ExpectedRevision,
    [param: Required, MaxLength(80)] string IdempotencyKey,
    bool ConfirmOverlap = false);

public sealed record ItineraryItemMutationResponse(
    bool Success,
    string Message,
    int Revision,
    ScheduleItemDto? Item = null,
    bool HasOverlap = false);
