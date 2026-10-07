using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

// Compare content directly: array references and generated timestamps are not catalog changes.
public static class MapContentComparison
{
    public static bool SameCatalog(IReadOnlyList<RecommendationDto> first, IReadOnlyList<RecommendationDto> second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first.Count != second.Count) return false;
        for (var i = 0; i < first.Count; i++) if (!SameRecommendation(first[i], second[i])) return false;
        return true;
    }

    public static bool SameRecommendation(RecommendationDto? first, RecommendationDto? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null) return false;
        return first.Id == second.Id && first.DestinationId == second.DestinationId && SameSearchInputs(first, second)
            && first.PriceLevel == second.PriceLevel && first.Latitude == second.Latitude && first.Longitude == second.Longitude
            && first.SuggestedDurationMinutes == second.SuggestedDurationMinutes && first.Rating == second.Rating
            && first.OpeningHours == second.OpeningHours && first.AccessLevel == second.AccessLevel
            && first.PackageIds.SequenceEqual(second.PackageIds) && first.DistanceKm == second.DistanceKm
            && first.Provider == second.Provider && first.EditorialReviewedOn == second.EditorialReviewedOn
            && first.SourceUrl == second.SourceUrl && first.ProviderPlaceId == second.ProviderPlaceId
            && first.Attribution == second.Attribution && first.ExtraDescription == second.ExtraDescription
            && first.ReservationInstructions == second.ReservationInstructions && first.OriginalPrice == second.OriginalPrice
            && first.IsPriceKnown == second.IsPriceKnown && first.RatingSource == second.RatingSource;
    }

    public static bool SameSearchInputs(RecommendationDto first, RecommendationDto second) =>
        first.Title == second.Title && first.Neighborhood == second.Neighborhood && first.Category == second.Category
        && first.RefinedType == second.RefinedType && first.Description == second.Description
        && first.Tags.SequenceEqual(second.Tags, StringComparer.Ordinal);

    public static bool SamePreview(FreeMapPreviewDto? first, FreeMapPreviewDto? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null || first.City != second.City || first.ContactUrl != second.ContactUrl
            || first.UnlockedCount != second.UnlockedCount || first.LockedCount != second.LockedCount
            || first.Markers.Count != second.Markers.Count) return false;
        for (var i = 0; i < first.Markers.Count; i++)
        {
            var a = first.Markers[i]; var b = second.Markers[i];
            if (a.MarkerKey != b.MarkerKey || a.Latitude != b.Latitude || a.Longitude != b.Longitude
                || a.Access != b.Access || !SameRecommendation(a.Recommendation, b.Recommendation)) return false;
        }
        return true;
    }
}
