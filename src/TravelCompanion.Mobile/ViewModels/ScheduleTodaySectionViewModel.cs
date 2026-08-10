using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class ScheduleTodaySectionViewModel(
    int dayNumber,
    string periodLabel,
    string description,
    IReadOnlyList<TodayLocationViewModel> locations,
    IReadOnlyList<TodayReservationViewModel> reservations)
{
    public string Title => $"Dia {dayNumber}";
    public string PeriodLabel { get; } = periodLabel;
    public string Description { get; } = description;
    public IReadOnlyList<TodayLocationViewModel> Locations { get; } = locations;
    public IReadOnlyList<TodayReservationViewModel> Reservations { get; } = reservations;
    public bool HasLocations => Locations.Count > 0;
    public bool HasReservations => Reservations.Count > 0;
    public bool HasContent => HasLocations || HasReservations;
}

public sealed class TodayLocationViewModel
{
    public TodayLocationViewModel(RecommendationDto recommendation, decimal? distanceKm)
    {
        Recommendation = recommendation;
        DistanceKm = distanceKm;
        Title = recommendation.Title;
        Detail = string.IsNullOrWhiteSpace(recommendation.Neighborhood)
            ? recommendation.Category
            : $"{recommendation.Category} · {recommendation.Neighborhood}";
        DistanceLabel = distanceKm.HasValue
            ? $"{distanceKm.Value:0.0} km desde tu ubicacion"
            : string.Empty;
        RankReason = string.Empty;
        VisitStatusLabel = string.Empty;
    }

    public TodayLocationViewModel(TodayRecommendationDto todayRecommendation)
        : this(todayRecommendation.Recommendation, todayRecommendation.DistanceKm)
    {
        RankReason = todayRecommendation.RankReason;
        VisitStatusLabel = todayRecommendation.VisitStatusLabel ?? string.Empty;
        IsVisited = todayRecommendation.IsVisited;
    }

    public RecommendationDto Recommendation { get; }
    public decimal? DistanceKm { get; }
    public string Title { get; }
    public string Detail { get; }
    public string DistanceLabel { get; }
    public string RankReason { get; }
    public string VisitStatusLabel { get; }
    public bool IsVisited { get; }
    public bool HasDistance => !string.IsNullOrWhiteSpace(DistanceLabel);
    public bool HasRankReason => !string.IsNullOrWhiteSpace(RankReason);
    public bool HasVisitStatus => !string.IsNullOrWhiteSpace(VisitStatusLabel);
}

public sealed class TodayReservationViewModel
{
    public TodayReservationViewModel(ScheduleItemDto item)
    {
        Item = item;
        TimeLabel = item.HasEnd
            ? $"{item.StartsAt:HH\\:mm} - {item.EndLabel}"
            : $"{item.StartsAt:HH\\:mm}";
        Title = item.Title;
        Detail = item.TypeLabel;
        Place = item.Type switch
        {
            TravelCompanion.Shared.ReservationType.Flight => item.MainDetail,
            _ => string.IsNullOrWhiteSpace(item.LocationName) ? item.Address : item.LocationName
        };
        Confirmation = string.IsNullOrWhiteSpace(item.ConfirmationCode)
            ? string.Empty
            : $"Codigo: {item.ConfirmationCode}";
    }

    public ScheduleItemDto Item { get; }
    public string TimeLabel { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Place { get; }
    public string Confirmation { get; }
    public bool HasConfirmation => !string.IsNullOrWhiteSpace(Confirmation);
}

internal sealed record TodayPeriod(
    string Label,
    TimeOnly Start,
    TimeOnly End,
    IReadOnlyList<string> Keywords)
{
    public static IReadOnlyList<TodayPeriod> All { get; } =
    [
        new("Mañana", new TimeOnly(5, 0), new TimeOnly(12, 0), ["coffee", "cafe", "breakfast", "temple", "shrine", "market", "walk", "culture"]),
        new("Medio dia", new TimeOnly(12, 0), new TimeOnly(15, 0), ["food", "lunch", "ramen", "sushi", "restaurant", "shopping", "market"]),
        new("Tarde", new TimeOnly(15, 0), new TimeOnly(20, 0), ["walk", "culture", "shopping", "museum", "garden", "route", "tea"]),
        new("Noche", new TimeOnly(20, 0), new TimeOnly(5, 0), ["dinner", "bar", "night", "izakaya", "food", "view", "dance"])
    ];

    public bool Contains(TimeOnly time)
    {
        return Start < End
            ? time >= Start && time < End
            : time >= Start || time < End;
    }
}
