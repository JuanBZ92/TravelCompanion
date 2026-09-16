using System.Globalization;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public static class RecommendationPresentation
{
    public static RecommendationDto ToDto(Recommendation item, decimal? distanceKm = null, string? locale = null)
    {
        var english = (locale ?? CultureInfo.CurrentUICulture.Name).StartsWith("en", StringComparison.OrdinalIgnoreCase);
        string? Text(string? es, string? en) => english && !string.IsNullOrWhiteSpace(en) ? en : es;
        return new RecommendationDto(item.Id, item.DestinationId, item.Title, item.Category,
            item.Neighborhood, Text(item.Description, item.DescriptionEn) ?? string.Empty, item.Tags,
            item.PriceLevel, item.Latitude, item.Longitude, item.SuggestedDurationMinutes, item.Rating,
            item.OpeningHours, item.AccessLevel, item.Packages.Select(p => p.Id).ToList(), distanceKm)
        {
            ProviderPlaceId = item.ProviderPlaceId,
            ExtraDescription = Text(item.ExtraDescription, item.ExtraDescriptionEn),
            RefinedType = Text(item.RefinedType, item.RefinedTypeEn),
            ReservationInstructions = Text(item.ReservationInstructions, item.ReservationInstructionsEn),
            OriginalPrice = item.OriginalPrice,
            IsPriceKnown = item.IsPriceKnown,
            RatingSource = item.SourceName?.StartsWith("YUKU Japan", StringComparison.Ordinal) == true ? "Tabelog" : null
        };
    }
}
