using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class TravelChatCardViewModel : ObservableObject
{
    private readonly TravelCardDto _card;
    private bool _isSaved;
    private string? _feedbackStatusMessage;

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
        LocalizationResourceManager.Instance.CultureChanged += OnCultureChanged;
    }

    public string Title => _card.Title;
    public string? Subtitle => _card.Subtitle;
    public string? Description => _card.Description;
    public string CostLabel => string.IsNullOrWhiteSpace(FormatCost(_card.EstimatedCost))
        ? string.Empty
        : $"{Resource("AssistantCostPrefix")}: {FormatCost(_card.EstimatedCost)}";
    public bool HasCostLabel => !string.IsNullOrWhiteSpace(CostLabel);
    public string DistanceLabel => _card.DistanceKm.HasValue
        ? $"{Resource("AssistantDistancePrefix")}: {_card.DistanceKm.Value.ToString("0.0", CultureInfo.CurrentCulture)} km"
        : string.Empty;
    public bool HasDistanceLabel => !string.IsNullOrWhiteSpace(DistanceLabel);
    public string WalkingLabel => _card.WalkingMinutes.HasValue
        ? $"{Resource("AssistantWalkingPrefix")}: {_card.WalkingMinutes.Value} min"
        : string.Empty;
    public bool HasWalkingLabel => !string.IsNullOrWhiteSpace(WalkingLabel);
    public Guid? RecommendationId { get; }
    public bool HasRecommendationId => RecommendationId.HasValue;
    public bool HasDetailAction => HasRecommendationId;
    public string RecommendationReference => RecommendationId?.ToString() ?? Title;
    public TimeOnly? StartsAt { get; }
    public TimeOnly? EndsAt { get; }
    public bool CanSave => RecommendationId.HasValue && StartsAt.HasValue && !IsSaved;
    public string SaveButtonText => IsSaved ? Resource("AssistantSavedButton") : Resource("AssistantSaveButton");
    public string DetailButtonText => Resource("AssistantDetailButton");
    public string NearbyButtonText => Resource("AssistantNearbyButton");
    public string ReplaceButtonText => Resource("AssistantReplaceButton");
    public string UsefulButtonText => Resource("AssistantUsefulButton");
    public string NotUsefulButtonText => Resource("AssistantNotUsefulButton");
    public string HideSimilarButtonText => Resource("AssistantHideSimilarButton");
    public bool HasFeedbackActions => HasRecommendationId;
    public string? FeedbackStatusMessage
    {
        get => _feedbackStatusMessage;
        set
        {
            if (SetProperty(ref _feedbackStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasFeedbackStatus));
            }
        }
    }
    public bool HasFeedbackStatus => !string.IsNullOrWhiteSpace(FeedbackStatusMessage);
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
            ? $"{Resource("AssistantTimePrefix")}: {_card.StartTime!}"
            : $"{Resource("AssistantTimePrefix")}: {_card.StartTime} - {_card.EndTime}";
    public bool HasTimeLabel => !string.IsNullOrWhiteSpace(TimeLabel);
    public IReadOnlyList<string> Tags => (_card.Tags ?? []).Take(4).ToList();
    public IReadOnlyList<TravelChatTagActionViewModel> TagActions => Tags
        .Select(tag => new TravelChatTagActionViewModel(tag))
        .ToList();
    public bool HasTags => Tags.Count > 0;
    public IReadOnlyList<string> WhyItFits => _card.WhyItFits.Take(2).ToList();
    public bool HasReasons => WhyItFits.Count > 0;
    public IReadOnlyList<string> Warnings => _card.Warnings.Take(1).ToList();
    public IReadOnlyList<string> WarningLabels => Warnings
        .Select(warning => $"{Resource("AssistantAttentionPrefix")}: {warning}")
        .ToList();
    public bool HasWarnings => Warnings.Count > 0;

    private static string FormatCost(string? cost)
    {
        return cost?.Trim().ToLowerInvariant() switch
        {
            null or "" => string.Empty,
            "free" or "gratis" => Resource("AssistantFreeCost"),
            "low" or "budget" or "cheap" or "barato" => Resource("AssistantLowCost"),
            "medium" or "moderate" or "medio" => Resource("AssistantMediumCost"),
            "high" or "expensive" or "premium" or "alto" => Resource("AssistantHighCost"),
            _ => cost.Trim()
        };
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CostLabel));
        OnPropertyChanged(nameof(DistanceLabel));
        OnPropertyChanged(nameof(WalkingLabel));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(DetailButtonText));
        OnPropertyChanged(nameof(NearbyButtonText));
        OnPropertyChanged(nameof(ReplaceButtonText));
        OnPropertyChanged(nameof(UsefulButtonText));
        OnPropertyChanged(nameof(NotUsefulButtonText));
        OnPropertyChanged(nameof(HideSimilarButtonText));
        OnPropertyChanged(nameof(TagActions));
        OnPropertyChanged(nameof(WarningLabels));
    }

    private static string Resource(string key)
    {
        return LocalizationResourceManager.Instance[key];
    }
}

public sealed class TravelChatTagActionViewModel(string tag)
{
    public string Tag { get; } = tag;
    public string Label => $"{LocalizationResourceManager.Instance["AssistantAvoidTagPrefix"]} #{Tag}";
}
