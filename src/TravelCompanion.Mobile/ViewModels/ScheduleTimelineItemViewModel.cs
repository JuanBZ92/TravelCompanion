using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class ScheduleTimelineItemViewModel
{
    public ScheduleTimelineItemViewModel(ScheduleItemDto item, int dayNumber)
    {
        Item = item;
        HeaderLabel = $"Dia {dayNumber} {GetPeriodLabel(item.StartsAt)} - {item.Title}";
        TypeBadge = "RESERVA";
        Title = item.Type == ReservationType.Flight
            ? item.MainDetail
            : item.LocationName;
        MetaLine = item.HasEnd
            ? $"{item.StartsAt:HH\\:mm} - {item.EndLabel} · {item.TypeLabel}"
            : $"{item.StartsAt:HH\\:mm} · {item.TypeLabel}";
        Body = GetReservationBody(item);
        DetailLine = item.Type == ReservationType.Flight
            ? item.SecondaryDetail
            : item.Address;
        MapLabel = "MAPA";
        ActionTitle = item.Type == ReservationType.Flight
            ? item.MainDetail
            : item.LocationName;
        ConfirmationLine = $"Codigo: {item.ConfirmationCode}";
    }

    public ScheduleTimelineItemViewModel(
        RecommendationDto recommendation,
        string periodLabel,
        string? personalDescription,
        decimal? distanceKm)
    {
        Recommendation = recommendation;
        HeaderLabel = periodLabel;
        TypeBadge = "RECOMENDADO";
        Title = recommendation.Title;
        MetaLine = CreateRecommendationMetaLine(recommendation);
        Body = personalDescription ?? CreateRecommendationDescription(recommendation, periodLabel, distanceKm);
        DetailLine = string.Empty;
        MapLabel = "MAP LOCATION";
        ActionTitle = CreateMapLocationLabel(recommendation, distanceKm);
        ConfirmationLine = string.Empty;
    }

    public ScheduleItemDto? Item { get; }
    public RecommendationDto? Recommendation { get; }
    public bool IsReservation => Item is not null;
    public bool IsRecommendation => Recommendation is not null;

    public string HeaderLabel { get; }
    public string TypeBadge { get; }
    public string Title { get; }
    public string MetaLine { get; }
    public string Body { get; }
    public string DetailLine { get; }
    public string MapLabel { get; }
    public string ActionTitle { get; }
    public string ConfirmationLine { get; }

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public bool HasDetailLine => !string.IsNullOrWhiteSpace(DetailLine);
    public bool HasActionTitle => !string.IsNullOrWhiteSpace(ActionTitle);
    public bool HasConfirmationCode => IsReservation && !string.IsNullOrWhiteSpace(Item?.ConfirmationCode);

    private static string GetReservationBody(ScheduleItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            return item.Notes.Trim();
        }

        return item.Type == ReservationType.Flight
            ? item.SecondaryDetail
            : item.Title;
    }

    private static string GetPeriodLabel(TimeOnly startsAt) =>
        startsAt.Hour switch
        {
            < 12 => "Mañana",
            < 15 => "Mediodia",
            < 20 => "Tarde",
            _ => "Noche"
        };

    private static string CreateRecommendationMetaLine(RecommendationDto recommendation)
    {
        var parts = new[]
        {
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.SuggestedDurationMinutes > 0
                ? $"{recommendation.SuggestedDurationMinutes} min"
                : null
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" · ", parts);
    }

    private static string CreateRecommendationDescription(
        RecommendationDto recommendation,
        string periodLabel,
        decimal? distanceKm)
    {
        var category = string.IsNullOrWhiteSpace(recommendation.Category)
            ? "plan"
            : recommendation.Category.Trim().ToLowerInvariant();
        var place = string.IsNullOrWhiteSpace(recommendation.Neighborhood)
            ? "cerca de tu recorrido"
            : $"en {recommendation.Neighborhood.Trim()}";
        var distance = distanceKm.HasValue
            ? $" Esta a {distanceKm.Value:0.0} km de donde estas."
            : string.Empty;

        return $"{periodLabel}: una opcion de {category} {place}, pensada para completar el dia sin cargar demasiado la agenda.{distance}";
    }

    private static string CreateMapLocationLabel(RecommendationDto recommendation, decimal? distanceKm)
    {
        var place = string.IsNullOrWhiteSpace(recommendation.Neighborhood)
            ? recommendation.Title
            : recommendation.Neighborhood.Trim();

        return distanceKm.HasValue
            ? $"{place} · {distanceKm.Value:0.0} km"
            : place;
    }

}
