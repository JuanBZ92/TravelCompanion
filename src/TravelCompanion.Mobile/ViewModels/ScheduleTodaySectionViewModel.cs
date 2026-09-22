using TravelCompanion.Shared.Dtos;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.ViewModels;

internal static class ItineraryNotePreview
{
    public static bool IsAssistantPlaceholder(string? text) => string.Equals(text?.Trim(),
        "Guardado desde Travel Assistant.", StringComparison.OrdinalIgnoreCase);

    public static string Resolve(string? personal, string? curated)
    {
        var text = curated?.Trim() ?? string.Empty;
        // Older assistant saves store this provenance text in the personal notes field.
        // Keep genuine traveler notes, but let editorial content replace that placeholder.
        var isAssistantPlaceholder = IsAssistantPlaceholder(personal);
        if (!string.IsNullOrWhiteSpace(personal) && (!isAssistantPlaceholder || text.Length == 0))
            return personal;
        return text.Length <= 180 ? text : text[..179].TrimEnd() + "…";
    }
}

internal static class ScheduleRecommendationFallback
{
    public static RecommendationDto Create(ScheduleItemDto item) => new(
        item.RecommendationId ?? item.Id,
        Guid.Empty,
        item.Title,
        "Lugar",
        string.IsNullOrWhiteSpace(item.Address) ? item.City : item.Address,
        item.CuratedNotes ?? string.Empty,
        [],
        "medium",
        item.Latitude ?? 0,
        item.Longitude ?? 0,
        item.DurationMinutes ?? 60,
        null,
        null,
        TravelCompanion.Shared.ContentAccessLevel.Free,
        [],
        null)
    {
        ProviderPlaceId = item.ProviderPlaceId
    };
}

public sealed record ScheduleTodayLoadingSectionViewModel(
    string Title,
    string PeriodLabel);

public sealed class ScheduleTodaySectionViewModel(
    int dayNumber,
    DateOnly date,
    string periodKey,
    string periodLabel,
    string description,
    IReadOnlyList<TodayLocationViewModel> locations,
    IReadOnlyList<TodayReservationViewModel> reservations,
    bool canAddItem = false)
{
    public string Title => $"Dia {dayNumber}";
    public string PeriodLabel { get; } = periodLabel;
    public DateOnly Date { get; } = date;
    public string PeriodKey { get; } = periodKey;
    public bool CanAddItem { get; } = canAddItem;
    public IReadOnlyList<TodayLocationViewModel> Locations { get; } = locations;
    public IReadOnlyList<TodayReservationViewModel> Reservations { get; } = reservations;
    public bool HasLocations => Locations.Count > 0;
    public bool HasReservations => Reservations.Count > 0;
    public bool HasContent => HasLocations || HasReservations;
    public bool IsEmpty => !HasContent;
    public bool ShowDescription => HasContent && HasDescription;
    public string EmptyTitle => LocalizationResourceManager.Instance["TodayEmptyTitle"];
    public string EmptySubtitle => LocalizationResourceManager.Instance["TodayEmptySubtitle"];
    public string EmptyHint => LocalizationResourceManager.Instance["TodayEmptyHint"];
    public string Description { get; } = NormalizeDescription(
        periodLabel,
        description,
        locations.Count > 0 || reservations.Count > 0);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    private static string NormalizeDescription(string periodLabel, string description, bool hasContent)
    {
        if (!hasContent)
        {
            return "Libre";
        }

        var value = description?.Trim() ?? string.Empty;
        var isGeneratedLoadedMessage = value.StartsWith($"{periodLabel}:", StringComparison.OrdinalIgnoreCase)
            && (value.Contains("ya tenes", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ya tenés", StringComparison.OrdinalIgnoreCase))
            && value.Contains("cargad", StringComparison.OrdinalIgnoreCase);

        return isGeneratedLoadedMessage ? string.Empty : value;
    }
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

    public TodayLocationViewModel(
        RecommendationDto recommendation,
        decimal? distanceKm,
        bool isAssigned = false,
        ScheduleItemDto? assignedItem = null)
    {
        Recommendation = recommendation;
        AssignedItem = assignedItem;
        DistanceKm = distanceKm;
        IsAssigned = isAssigned;
        Title = assignedItem?.Title ?? recommendation.Title;
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
        AssignmentLabel = string.Empty;
    }

    public TodayLocationViewModel(
        TodayRecommendationDto todayRecommendation,
        ScheduleItemDto? assignedItem = null,
        decimal? distanceOverrideKm = null)
        : this(
            todayRecommendation.Recommendation,
            distanceOverrideKm ?? todayRecommendation.DistanceKm,
            todayRecommendation.IsAssigned,
            assignedItem)
    {
        RankReason = todayRecommendation.RankReason;
        VisitStatusLabel = todayRecommendation.VisitStatusLabel ?? string.Empty;
        IsVisited = todayRecommendation.IsVisited;
    }

    public RecommendationDto Recommendation { get; }
    public ScheduleItemDto? AssignedItem { get; }
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
    public bool CanRemove => AssignedItem?.IsTravelerOwned == true;
    public bool CanEdit => CanRemove;
    public string Notes => ItineraryNotePreview.Resolve(AssignedItem?.Notes, IsAssigned ? Recommendation.DisplayDescription : null);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasDistance => !string.IsNullOrWhiteSpace(DistanceLabel);
    public bool HasRankReason => false;
    public bool HasVisitStatus => !IsAssigned && !string.IsNullOrWhiteSpace(VisitStatusLabel);
    public bool HasAssignmentLabel => !string.IsNullOrWhiteSpace(AssignmentLabel);

    private static string GetRefinedCategory(RecommendationDto recommendation)
    {
        if (!string.IsNullOrWhiteSpace(recommendation.RefinedType)) return recommendation.RefinedType;
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
    public TodayReservationViewModel(
        ScheduleItemDto item,
        TodayHotelBaseDto? hotelBase = null,
        bool canCalculateRoutes = false)
    {
        Item = item;
        TimeLabel = item.HasExactTime
            ? item.HasEnd
                ? $"{item.StartsAt:HH\\:mm} - {item.EndLabel}"
                : $"{item.StartsAt:HH\\:mm}"
            : item.PeriodDisplay;
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
        DistanceFromHotelLabel = CalculateDistanceLabel(hotelBase, item);
        HasRoutes = canCalculateRoutes && item.HasExactTime
            && (!string.IsNullOrWhiteSpace(item.ProviderPlaceId) || item.Latitude.HasValue && item.Longitude.HasValue);
        Routes =
        [
            new("WALK", "A pie", "route_walk.svg", item.Id),
            new("TRANSIT", "Transporte", "route_bus.svg", item.Id),
            new("DRIVE", "Auto", "route_car.svg", item.Id)
        ];
        CurrentLocationRoute = new("WALK", "Desde mi ubicación", "route_walk.svg", item.Id, useCurrentLocation: true);
    }

    public ScheduleItemDto Item { get; }
    public string TimeLabel { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Place { get; }
    public bool HasPlace => !string.IsNullOrWhiteSpace(Place)
        && !string.Equals(Place.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase);
    public string Confirmation { get; }
    public bool HasConfirmation => !string.IsNullOrWhiteSpace(Confirmation);
    public string DistanceFromHotelLabel { get; }
    public bool HasDistanceFromHotel => !string.IsNullOrWhiteSpace(DistanceFromHotelLabel);
    public bool CanEdit => Item.IsTravelerOwned;
    public string Notes => ItineraryNotePreview.Resolve(Item.Notes, Item.CuratedNotes);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasRoutes { get; }
    public IReadOnlyList<ItineraryRouteViewModel> Routes { get; }
    public ItineraryRouteViewModel CurrentLocationRoute { get; }

    private static string CalculateDistanceLabel(TodayHotelBaseDto? hotel, ScheduleItemDto item)
    {
        if (hotel?.Latitude is null || hotel.Longitude is null || item.Latitude is null || item.Longitude is null)
        {
            return string.Empty;
        }

        var distance = StraightLineDistanceCache.GetOrCalculate(
            hotel.Latitude.Value,
            hotel.Longitude.Value,
            item.Latitude.Value,
            item.Longitude.Value);
        return $"≈ {distance:0.0} km en línea recta desde el hotel";
    }
}

public sealed class ItineraryRouteViewModel(
    string mode,
    string label,
    string icon,
    Guid itemId = default,
    bool useCurrentLocation = false) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public Guid ItemId { get; } = itemId;
    public bool UseCurrentLocation { get; } = useCurrentLocation;
    public string Mode { get; } = mode;
    public string Label { get; } = label;
    public string Icon { get; } = icon;
    private string _duration = "Tocar para calcular";
    private string _departure = "";
    private string _origin = "";
    public string Duration { get => _duration; private set => SetProperty(ref _duration, value); }
    public string Departure { get => _departure; private set => SetProperty(ref _departure, value); }
    public string Origin { get => _origin; private set => SetProperty(ref _origin, value); }
    public void MarkLoading()
    {
        Duration = "Calculando...";
        Departure = string.Empty;
        Origin = string.Empty;
    }
    public void Apply(ItineraryRouteDto? route)
    {
        var originMarker = route?.OriginKind switch
        {
            "Hotel" => " (H)",
            "CurrentLocation" => " (U)",
            _ => string.Empty
        };
        Duration = route?.Status == "Available" ? $"{route.Minutes} min{originMarker}" : route?.Status switch
        {
            "TooEarly" => "Aun no disponible",
            "NoLocation" => "Sin ubicacion",
            "Past" => "Horario pasado",
            _ => "No disponible"
        };
        Departure = route?.Status == "Available"
            ? $"{(route.DeparturePassed ? "Salida pasada" : "Salir")} {route.DepartureLabel}" : "";
        Origin = string.IsNullOrWhiteSpace(route?.Origin) ? "" : $"Desde {route.Origin}";
    }
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
