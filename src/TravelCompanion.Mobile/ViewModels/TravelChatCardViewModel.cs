using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatCardViewModel : ObservableObject
{
    private readonly TravelCardDto _card;
    private bool _isSaved;

    public TravelChatCardViewModel(TravelCardDto card)
    {
        _card = card;
        RecommendationId = Guid.TryParse(card.RecommendationId, out var recommendationId)
            ? recommendationId
            : null;
        StartsAt = TimeOnly.TryParse(card.StartTime, out var startsAt)
            ? startsAt
            : null;
        EndsAt = TimeOnly.TryParse(card.EndTime, out var endsAt)
            ? endsAt
            : null;
    }

    public string Title => _card.Title;
    public string? Subtitle => _card.Subtitle;
    public string? Description => _card.Description;
    public string CostLabel => string.IsNullOrWhiteSpace(FormatCost(_card.EstimatedCost))
        ? string.Empty
        : $"Coste: {FormatCost(_card.EstimatedCost)}";
    public bool HasCostLabel => !string.IsNullOrWhiteSpace(CostLabel);
    public string DistanceLabel => _card.DistanceKm.HasValue
        ? $"Distancia: {_card.DistanceKm.Value.ToString("0.0", CultureInfo.CurrentCulture)} km"
        : string.Empty;
    public bool HasDistanceLabel => !string.IsNullOrWhiteSpace(DistanceLabel);
    public string WalkingLabel => _card.WalkingMinutes.HasValue
        ? $"Caminata: {_card.WalkingMinutes.Value} min"
        : string.Empty;
    public bool HasWalkingLabel => !string.IsNullOrWhiteSpace(WalkingLabel);
    public Guid? RecommendationId { get; }
    public bool HasRecommendationId => RecommendationId.HasValue;
    public TimeOnly? StartsAt { get; }
    public TimeOnly? EndsAt { get; }
    public bool CanSave => RecommendationId.HasValue && StartsAt.HasValue && !IsSaved;
    public string SaveButtonText => IsSaved ? "Guardado" : "Guardar";
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (SetProperty(ref _isSaved, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }
    public string TimeLabel => string.IsNullOrWhiteSpace(_card.StartTime)
        ? string.Empty
        : string.IsNullOrWhiteSpace(_card.EndTime)
            ? $"Horario: {_card.StartTime!}"
            : $"Horario: {_card.StartTime} - {_card.EndTime}";
    public bool HasTimeLabel => !string.IsNullOrWhiteSpace(TimeLabel);
    public IReadOnlyList<string> Tags => (_card.Tags ?? []).Take(6).ToList();
    public bool HasTags => Tags.Count > 0;
    public IReadOnlyList<string> WhyItFits => _card.WhyItFits;
    public bool HasReasons => WhyItFits.Count > 0;
    public IReadOnlyList<string> Warnings => _card.Warnings;
    public bool HasWarnings => Warnings.Count > 0;

    private static string FormatCost(string? cost)
    {
        return cost?.Trim().ToLowerInvariant() switch
        {
            null or "" => string.Empty,
            "free" or "gratis" => "Gratis",
            "low" or "budget" or "cheap" or "barato" => "Bajo",
            "medium" or "moderate" or "medio" => "Medio",
            "high" or "expensive" or "premium" or "alto" => "Alto",
            _ => cost.Trim()
        };
    }
}
