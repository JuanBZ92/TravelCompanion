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
    private static readonly IReadOnlyDictionary<string, string> RefinedTagLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sushi"] = "Sushi",
            ["ramen"] = "Ramen",
            ["cafe"] = "Café",
            ["breakfast"] = "Desayuno",
            ["brunch"] = "Brunch",
            ["tempura"] = "Tempura",
            ["soba"] = "Soba",
            ["udon"] = "Udon",
            ["gyoza"] = "Gyoza",
            ["yakiniku"] = "Yakiniku",
            ["yakitori"] = "Yakitori",
            ["tonkatsu"] = "Tonkatsu",
            ["unagi"] = "Unagi",
            ["kaiseki"] = "Kaiseki",
            ["pizza"] = "Pizza",
            ["burger"] = "Hamburguesas",
            ["market"] = "Mercado",
            ["tea"] = "Té",
            ["bar"] = "Bar",
            ["izakaya"] = "Izakaya",
            ["temple"] = "Templo",
            ["shrine"] = "Santuario",
            ["museum"] = "Museo",
            ["garden"] = "Jardín",
            ["shopping"] = "Compras"
        };

    private static readonly HashSet<string> GenericTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "food",
        "nightlife",
        "restaurant",
        "reservation required",
        "reservation recommended",
        "walk-in",
        "premium",
        "low",
        "medium",
        "high"
    };

    public TodayLocationViewModel(RecommendationDto recommendation, decimal? distanceKm, bool isAssigned = false)
    {
        Recommendation = recommendation;
        DistanceKm = distanceKm;
        IsAssigned = isAssigned;
        Title = recommendation.Title;
        RefinedCategory = GetRefinedCategory(recommendation);
        Detail = string.IsNullOrWhiteSpace(recommendation.Neighborhood)
            ? RefinedCategory
            : string.IsNullOrWhiteSpace(RefinedCategory)
                ? recommendation.Neighborhood
                : $"{RefinedCategory} · {recommendation.Neighborhood}";
        DistanceLabel = distanceKm.HasValue
            ? $"{distanceKm.Value:0.0} km desde tu ubicacion"
            : string.Empty;
        RankReason = string.Empty;
        VisitStatusLabel = string.Empty;
        AssignmentLabel = isAssigned ? "RECOMENDACION CURADA" : string.Empty;
    }

    public TodayLocationViewModel(TodayRecommendationDto todayRecommendation)
        : this(todayRecommendation.Recommendation, todayRecommendation.DistanceKm, todayRecommendation.IsAssigned)
    {
        RankReason = todayRecommendation.RankReason;
        VisitStatusLabel = todayRecommendation.VisitStatusLabel ?? string.Empty;
        IsVisited = todayRecommendation.IsVisited;
    }

    public RecommendationDto Recommendation { get; }
    public decimal? DistanceKm { get; }
    public string Title { get; }
    public string RefinedCategory { get; }
    public string Detail { get; }
    public string DistanceLabel { get; }
    public string RankReason { get; }
    public string VisitStatusLabel { get; }
    public string AssignmentLabel { get; }
    public bool IsVisited { get; }
    public bool IsAssigned { get; }
    public bool CanDismiss => !IsAssigned;
    public bool CanMarkVisited => !IsAssigned;
    public bool HasDistance => !string.IsNullOrWhiteSpace(DistanceLabel);
    public bool HasRankReason => false;
    public bool HasVisitStatus => !IsAssigned && !string.IsNullOrWhiteSpace(VisitStatusLabel);
    public bool HasAssignmentLabel => !string.IsNullOrWhiteSpace(AssignmentLabel);

    private static string GetRefinedCategory(RecommendationDto recommendation)
    {
        foreach (var tag in recommendation.Tags)
        {
            if (RefinedTagLabels.TryGetValue(tag, out var label))
            {
                return label;
            }
        }

        var specificTag = recommendation.Tags.FirstOrDefault(tag => !GenericTags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(specificTag))
        {
            return specificTag.Replace('-', ' ').Trim();
        }

        return recommendation.Category.Equals("Food", StringComparison.OrdinalIgnoreCase)
            ? "Gastronomía"
            : recommendation.Category;
    }
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
